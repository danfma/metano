using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Lowers one deconstruction into three ordered statement lists, which is
    /// C#'s evaluation order: the receivers and indices of the existing
    /// variables written, then the right side — every element of a tuple
    /// literal first, then each <c>Deconstruct</c> call, depth first — with
    /// the fresh locals, then the writes into existing variables.
    /// </summary>
    private sealed class DeconstructionLowering(IrExpressionExtractor extractor, SyntaxNode site)
    {
        private readonly List<IrStatement> _receivers = [];

        private readonly List<IrStatement> _bindings = [];

        private readonly Dictionary<StoredSlot, IrStatement> _writes = [];

        private readonly Dictionary<StoredSlot, IrExpression?> _places = [];

        private bool _failed;

        /// <summary>The statements that deconstruct the value written as <paramref name="value"/>.</summary>
        public IReadOnlyList<IrStatement> Lower(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            ExpressionSyntax value
        )
        {
            ResolvePlaces(slots);

            if (extractor.ShapeOf(info, site) is not { } shape)
                _failed = true;
            else if (shape is PositionalShape && LiteralOf(value, slots.Count) is { } literal)
                BindLiteral(slots, info, literal);
            else
                BindValue(PartOf(extractor.Extract(value), slots, info, shape, value));

            return Result();
        }

        /// <summary>The statements that deconstruct the already extracted <paramref name="value"/>.</summary>
        public IReadOnlyList<IrStatement> LowerValue(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            IrExpression value,
            IrTypeRef? valueType,
            ITypeSymbol? valueSymbol
        )
        {
            ResolvePlaces(slots);

            if (extractor.ShapeOf(info, site) is { } shape)
                BindValue(new Part(value, slots, info, shape, valueType, valueSymbol));
            else
                _failed = true;

            return Result();
        }

        private IReadOnlyList<IrStatement> Result() =>
            _failed
                ? [new IrExpressionStatement(new IrUnsupportedExpression("deconstruction"))]
                : [.. _receivers, .. _bindings, .. WritesInOrder()];

        /// <summary>The writes, left to right as the slots are written in C#.</summary>
        private IEnumerable<IrStatement> WritesInOrder() =>
            _places.Keys.Where(_writes.ContainsKey).Select(slot => _writes[slot]);

        private Part PartOf(
            IrExpression value,
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            DeconstructionShape shape,
            ExpressionSyntax source
        ) =>
            new(
                value,
                slots,
                info,
                shape,
                extractor.MapExpressionType(source),
                extractor._semantic.GetTypeInfo(source).Type
            );

        /// <summary>
        /// A tuple literal taken apart by position: every element is evaluated
        /// first, then the parts that a further deconstruction takes apart.
        /// </summary>
        private void BindLiteral(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            TupleExpressionSyntax literal
        )
        {
            var deferred = new List<Part>();

            BindElements(slots, info, literal, deferred);

            foreach (var part in deferred)
                BindValue(part);
        }

        private void BindElements(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            TupleExpressionSyntax literal,
            List<Part> deferred
        )
        {
            for (var index = 0; index < slots.Count; index++)
            {
                BindElement(
                    slots[index],
                    NestedInfo(info, index),
                    literal.Arguments[index].Expression,
                    deferred
                );
            }
        }

        private void BindElement(
            DeconstructionSlot slot,
            DeconstructionInfo info,
            ExpressionSyntax element,
            List<Part> deferred
        )
        {
            switch (slot)
            {
                case NestedSlot nested:
                    BindNestedElement(nested, info, element, deferred);
                    break;

                case DeclaredSlot declared:
                    _bindings.Add(
                        new IrVariableDeclaration(
                            declared.Name,
                            Type: null,
                            extractor.Extract(element),
                            InferredType: declared.Type
                        )
                    );

                    break;

                case StoredSlot stored:
                    BindStoredElement(stored, element);
                    break;

                default:
                    Evaluate(extractor.Extract(element));
                    break;
            }
        }

        /// <summary>
        /// A nested literal's elements join the enclosing literal's; any other
        /// element is held in a temporary and taken apart once every element
        /// has been evaluated.
        /// </summary>
        private void BindNestedElement(
            NestedSlot nested,
            DeconstructionInfo info,
            ExpressionSyntax element,
            List<Part> deferred
        )
        {
            if (extractor.ShapeOf(info, site) is not { } shape)
            {
                _failed = true;

                return;
            }

            if (shape is PositionalShape && LiteralOf(element, nested.Slots.Count) is { } literal)
            {
                BindElements(nested.Slots, info, literal, deferred);

                return;
            }

            var value = Bound(extractor.Extract(element), _bindings);

            deferred.Add(PartOf(value, nested.Slots, info, shape, element));
        }

        /// <summary>
        /// A constant is written as is; any other element is held in a
        /// temporary until every element has been evaluated.
        /// </summary>
        private void BindStoredElement(StoredSlot stored, ExpressionSyntax element)
        {
            var place = PlaceOf(stored);
            var value = extractor.Extract(element);

            if (place is null)
                return;

            Write(place, value is IrLiteral ? value : Bound(value, _bindings), stored);
        }

        /// <summary>
        /// Takes <paramref name="part"/> apart: one destructuring binding (or
        /// the <c>Deconstruct</c> call alone when it binds nothing), the
        /// conversions its parts need, then the parts that a further
        /// <c>Deconstruct</c> call takes apart, depth first.
        /// </summary>
        private void BindValue(Part part)
        {
            var parts = part.Shape is MethodShape method
                ? extractor.DeconstructCall(part.Value, method.Method)
                : part.Value;

            var deferred = new List<Part>();
            var conversions = new List<IrStatement>();
            var elements = ElementsOf(part.Slots, part.Info, part.Type, deferred, conversions);

            if (_failed)
                return;

            if (IrDeconstructionElement.BoundNamesOf(elements).Any())
            {
                _bindings.Add(
                    new IrTupleDeconstruction(elements, parts, IsConst: false)
                    {
                        Members = (part.Shape as MembersShape)?.Members,
                        ValueType = part.Shape is PositionalShape ? part.IrType : null,
                    }
                );
            }
            else
            {
                Evaluate(parts);
            }

            _bindings.AddRange(conversions);

            foreach (var nested in deferred)
                BindValue(nested);
        }

        private List<IrDeconstructionElement> ElementsOf(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            ITypeSymbol? valueType,
            List<Part> deferred,
            List<IrStatement> conversions
        )
        {
            var partTypes = PartTypesOf(info, valueType, slots.Count);

            return slots
                .Select(
                    (slot, index) =>
                        ElementOf(
                            slot,
                            NestedInfo(info, index),
                            partTypes[index],
                            deferred,
                            conversions
                        )
                )
                .ToList();
        }

        /// <summary>
        /// The element binding <paramref name="slot"/>'s part, of type
        /// <paramref name="partType"/>. A part that converts on its way to its
        /// variable, or is written into an existing one, is bound to a
        /// temporary; so is a part a <c>Deconstruct</c> call takes apart, after
        /// the enclosing binding (<paramref name="deferred"/>).
        /// </summary>
        private IrDeconstructionElement ElementOf(
            DeconstructionSlot slot,
            DeconstructionInfo info,
            ITypeSymbol? partType,
            List<Part> deferred,
            List<IrStatement> conversions
        ) =>
            slot switch
            {
                DeclaredSlot declared => DeclaredElementOf(declared, partType, conversions),
                StoredSlot stored => StoredElementOf(stored, partType),
                NestedSlot nested => NestedElementOf(nested, info, partType, deferred, conversions),
                _ => new IrDeconstructionElement(Name: null),
            };

        private IrDeconstructionElement DeclaredElementOf(
            DeclaredSlot declared,
            ITypeSymbol? partType,
            List<IrStatement> conversions
        )
        {
            if (!extractor.ConvertsPart(partType, declared.Symbol))
                return new IrDeconstructionElement(declared.Name);

            var temporary = extractor.NextDeconstructionTempName();

            conversions.Add(
                new IrVariableDeclaration(
                    declared.Name,
                    Type: null,
                    ConvertedPart(new IrIdentifier(temporary), partType, declared.Symbol),
                    InferredType: declared.Type
                )
            );

            return new IrDeconstructionElement(temporary);
        }

        private IrDeconstructionElement StoredElementOf(StoredSlot stored, ITypeSymbol? partType)
        {
            var temporary = extractor.NextDeconstructionTempName();

            if (PlaceOf(stored) is { } place)
            {
                var value = ConvertedPart(
                    new IrIdentifier(temporary),
                    partType,
                    extractor._semantic.GetTypeInfo(stored.Target).Type
                );

                Write(place, value, stored);
            }

            return new IrDeconstructionElement(temporary);
        }

        private IrDeconstructionElement NestedElementOf(
            NestedSlot nested,
            DeconstructionInfo info,
            ITypeSymbol? partType,
            List<Part> deferred,
            List<IrStatement> conversions
        )
        {
            var shape = extractor.ShapeOf(info, site);

            if (shape is null)
            {
                _failed = true;

                return new IrDeconstructionElement(Name: null);
            }

            if (shape is MethodShape)
            {
                var temporary = extractor.NextDeconstructionTempName();

                deferred.Add(
                    new Part(new IrIdentifier(temporary), nested.Slots, info, shape, null, partType)
                );

                return new IrDeconstructionElement(temporary);
            }

            return new IrDeconstructionElement(Name: null)
            {
                Nested = new IrNestedDeconstruction(
                    ElementsOf(nested.Slots, info, partType, deferred, conversions),
                    (shape as MembersShape)?.Members
                ),
            };
        }

        private IrExpression ConvertedPart(
            IrExpression value,
            ITypeSymbol? from,
            ITypeSymbol? to
        ) => extractor.ConvertedPart(site, value, from, to);

        /// <summary>
        /// Resolves the place every written slot stores into, left to right,
        /// so their receivers and indices are evaluated before the right side.
        /// </summary>
        private void ResolvePlaces(IReadOnlyList<DeconstructionSlot> slots)
        {
            foreach (var slot in slots)
            {
                if (slot is NestedSlot nested)
                    ResolvePlaces(nested.Slots);
                else if (slot is StoredSlot stored)
                    _places[stored] = PlaceAheadOf(stored);
            }
        }

        private IrExpression? PlaceOf(StoredSlot stored) => _places[stored];

        /// <summary>
        /// The place <paramref name="stored"/> writes, its receiver and index
        /// evaluated ahead of the right side: even a local, which the right
        /// side may reassign, is read once there.
        /// </summary>
        private IrExpression? PlaceAheadOf(StoredSlot stored)
        {
            if (extractor.StorePlaceOf(stored.Target) is not { } place)
            {
                _failed = true;

                return null;
            }

            return place switch
            {
                IrMemberAccess member => member with { Target = Fixed(member.Target) },
                IrElementAccess element => element with
                {
                    Target = Fixed(element.Target),
                    Index = Fixed(element.Index),
                },
                _ => place,
            };
        }

        private IrExpression Fixed(IrExpression operand) =>
            IsFixedOperand(operand) ? operand : Bound(operand, _receivers);

        private IrIdentifier Bound(IrExpression value, List<IrStatement> statements)
        {
            var name = extractor.NextDeconstructionTempName();

            statements.Add(new IrVariableDeclaration(name, Type: null, value));

            return new IrIdentifier(name);
        }

        private void Write(IrExpression place, IrExpression value, StoredSlot stored)
        {
            var write = new IrBinaryExpression(place, IrBinaryOp.Assign, value);

            _writes[stored] = new IrExpressionStatement(
                place is IrElementAccess && !extractor._context.KeepsNativeCollections
                    ? write with
                    {
                        Location = SourceLocationOf(stored.Target),
                    }
                    : write
            );
        }

        /// <summary>A part no slot binds is still evaluated for its effects.</summary>
        private void Evaluate(IrExpression value)
        {
            if (!IsStableOperand(value))
                _bindings.Add(new IrExpressionStatement(value));
        }

        /// <summary>
        /// <paramref name="value"/> as a tuple literal of <paramref name="count"/>
        /// elements, through any parentheses; <c>null</c> otherwise.
        /// </summary>
        private static TupleExpressionSyntax? LiteralOf(ExpressionSyntax value, int count) =>
            value switch
            {
                ParenthesizedExpressionSyntax parenthesized => LiteralOf(
                    parenthesized.Expression,
                    count
                ),
                TupleExpressionSyntax tuple when tuple.Arguments.Count == count => tuple,
                _ => null,
            };

        private static DeconstructionInfo NestedInfo(DeconstructionInfo info, int index) =>
            index < info.Nested.Length ? info.Nested[index] : default;

        /// <summary>
        /// A value to take apart and how: its slots, its deconstruction and
        /// shape, and its type as the IR and as Roslyn see it.
        /// </summary>
        private sealed record Part(
            IrExpression Value,
            IReadOnlyList<DeconstructionSlot> Slots,
            DeconstructionInfo Info,
            DeconstructionShape Shape,
            IrTypeRef? IrType,
            ITypeSymbol? Type
        );
    }
}
