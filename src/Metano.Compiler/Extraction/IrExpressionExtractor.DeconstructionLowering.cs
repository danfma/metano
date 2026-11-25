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
    /// variables written, then the right side with its <c>Deconstruct</c>
    /// calls and the fresh locals, then the writes into existing variables.
    /// </summary>
    private sealed class DeconstructionLowering(IrExpressionExtractor extractor, SyntaxNode site)
    {
        private readonly List<IrStatement> _receivers = [];

        private readonly List<IrStatement> _bindings = [];

        private readonly List<IrStatement> _writes = [];

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
            BindSyntax(slots, info, value);

            return Result();
        }

        /// <summary>The statements that deconstruct the already extracted <paramref name="value"/>.</summary>
        public IReadOnlyList<IrStatement> LowerValue(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            IrExpression value,
            IrTypeRef? valueType
        )
        {
            ResolvePlaces(slots);

            if (extractor.ShapeOf(info, site) is { } shape)
                BindValue(slots, info, shape, value, valueType);
            else
                _failed = true;

            return Result();
        }

        private IReadOnlyList<IrStatement> Result() =>
            _failed
                ? [new IrExpressionStatement(new IrUnsupportedExpression("deconstruction"))]
                : [.. _receivers, .. _bindings, .. _writes];

        /// <summary>
        /// A tuple literal taken apart by position binds each element to its
        /// slot directly; any other value is extracted whole.
        /// </summary>
        private void BindSyntax(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            ExpressionSyntax value
        )
        {
            if (extractor.ShapeOf(info, site) is not { } shape)
            {
                _failed = true;

                return;
            }

            if (
                shape is PositionalShape
                && UnparenthesizedTuple(value) is { } literal
                && literal.Arguments.Count == slots.Count
            )
            {
                for (var index = 0; index < slots.Count; index++)
                    BindElement(slots[index], NestedInfo(info, index), literal.Arguments[index]);

                return;
            }

            BindValue(
                slots,
                info,
                shape,
                extractor.Extract(value),
                extractor.MapExpressionType(value)
            );
        }

        private void BindElement(
            DeconstructionSlot slot,
            DeconstructionInfo info,
            ArgumentSyntax element
        )
        {
            switch (slot)
            {
                case NestedSlot nested:
                    BindSyntax(nested.Slots, info, element.Expression);
                    break;

                case DeclaredSlot declared:
                    _bindings.Add(
                        new IrVariableDeclaration(
                            declared.Name,
                            Type: null,
                            extractor.Extract(element.Expression),
                            InferredType: declared.Type
                        )
                    );

                    break;

                case StoredSlot stored:
                    BindStoredElement(stored, element.Expression);
                    break;

                default:
                    Evaluate(extractor.Extract(element.Expression));
                    break;
            }
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

        private void BindValue(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            DeconstructionShape shape,
            IrExpression value,
            IrTypeRef? valueType = null
        )
        {
            var parts = shape is MethodShape method
                ? extractor.DeconstructCall(value, method.Method)
                : value;

            var deferred = new List<DeferredPart>();
            var elements = ElementsOf(slots, info, deferred);

            if (_failed)
                return;

            if (IrDeconstructionElement.BoundNamesOf(elements).Any())
            {
                _bindings.Add(
                    new IrTupleDeconstruction(elements, parts, IsConst: false)
                    {
                        Members = (shape as MembersShape)?.Members,
                        ValueType = shape is PositionalShape ? valueType : null,
                    }
                );
            }
            else
            {
                Evaluate(parts);
            }

            foreach (var part in deferred)
                BindValue(part.Slots, part.Info, part.Shape, new IrIdentifier(part.Name));
        }

        private List<IrDeconstructionElement> ElementsOf(
            IReadOnlyList<DeconstructionSlot> slots,
            DeconstructionInfo info,
            List<DeferredPart> deferred
        ) =>
            slots
                .Select((slot, index) => ElementOf(slot, NestedInfo(info, index), deferred))
                .ToList();

        /// <summary>
        /// The element binding <paramref name="slot"/>'s part. A part taken
        /// apart by a <c>Deconstruct</c> call is bound to a temporary and
        /// deconstructed after the enclosing binding (<paramref name="deferred"/>).
        /// </summary>
        private IrDeconstructionElement ElementOf(
            DeconstructionSlot slot,
            DeconstructionInfo info,
            List<DeferredPart> deferred
        )
        {
            switch (slot)
            {
                case DeclaredSlot declared:
                    return new IrDeconstructionElement(declared.Name);

                case StoredSlot stored:
                    var temporary = extractor.NextDeconstructionTempName();

                    if (PlaceOf(stored) is { } place)
                        Write(place, new IrIdentifier(temporary), stored);

                    return new IrDeconstructionElement(temporary);

                case NestedSlot nested:
                    return NestedElementOf(nested, info, deferred);

                default:
                    return new IrDeconstructionElement(Name: null);
            }
        }

        private IrDeconstructionElement NestedElementOf(
            NestedSlot nested,
            DeconstructionInfo info,
            List<DeferredPart> deferred
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

                deferred.Add(new DeferredPart(temporary, nested.Slots, info, shape));

                return new IrDeconstructionElement(temporary);
            }

            return new IrDeconstructionElement(Name: null)
            {
                Nested = new IrNestedDeconstruction(
                    ElementsOf(nested.Slots, info, deferred),
                    (shape as MembersShape)?.Members
                ),
            };
        }

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
        /// evaluated ahead of the right side unless reading them again is
        /// harmless.
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
                IrMemberAccess member => member with { Target = Stable(member.Target) },
                IrElementAccess element => element with
                {
                    Target = Stable(element.Target),
                    Index = Stable(element.Index),
                },
                _ => place,
            };
        }

        private IrExpression Stable(IrExpression operand) =>
            IsStableOperand(operand) ? operand : Bound(operand, _receivers);

        private IrIdentifier Bound(IrExpression value, List<IrStatement> statements)
        {
            var name = extractor.NextDeconstructionTempName();

            statements.Add(new IrVariableDeclaration(name, Type: null, value));

            return new IrIdentifier(name);
        }

        private void Write(IrExpression place, IrExpression value, StoredSlot stored)
        {
            var write = new IrBinaryExpression(place, IrBinaryOp.Assign, value);

            _writes.Add(
                new IrExpressionStatement(
                    place is IrElementAccess && !extractor._context.KeepsNativeCollections
                        ? write with
                        {
                            Location = SourceLocationOf(stored.Target),
                        }
                        : write
                )
            );
        }

        /// <summary>A part no slot binds is still evaluated for its effects.</summary>
        private void Evaluate(IrExpression value)
        {
            if (!IsStableOperand(value))
                _bindings.Add(new IrExpressionStatement(value));
        }

        private static TupleExpressionSyntax? UnparenthesizedTuple(ExpressionSyntax value) =>
            value switch
            {
                ParenthesizedExpressionSyntax parenthesized => UnparenthesizedTuple(
                    parenthesized.Expression
                ),
                TupleExpressionSyntax tuple => tuple,
                _ => null,
            };

        private static DeconstructionInfo NestedInfo(DeconstructionInfo info, int index) =>
            index < info.Nested.Length ? info.Nested[index] : default;

        private sealed record DeferredPart(
            string Name,
            IReadOnlyList<DeconstructionSlot> Slots,
            DeconstructionInfo Info,
            DeconstructionShape Shape
        );
    }
}
