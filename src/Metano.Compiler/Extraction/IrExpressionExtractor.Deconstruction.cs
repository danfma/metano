using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Deconstruction in statement position — <c>var (a, b) = e;</c>,
/// <c>(x, var y) = e;</c>, <c>(a, (b, c)) = e;</c>, <c>foreach (var (k, v) in
/// d)</c> — lowered to statements that keep C#'s evaluation order: the
/// receivers and indices of the existing variables written are evaluated
/// first, then the right side in full (with every <c>Deconstruct</c> call),
/// then the writes, left to right.
/// <list type="bullet">
///   <item>Fresh locals bind through an <see cref="IrTupleDeconstruction"/>,
///   read by position (a tuple, an array, <c>KeyValuePair</c>, a
///   <c>[JsTuple]</c> record), by member (a record's synthesized
///   <c>Deconstruct</c>) or by position from the multi-value result of a
///   user <c>Deconstruct</c> call (<see cref="IrOutArgumentCall"/>).</item>
///   <item>A tuple literal on the right side is taken apart element by
///   element, so no tuple value is built only to be read back.</item>
///   <item>An existing variable is written after everything else is
///   evaluated, from a temporary the deconstruction binds.</item>
/// </list>
/// A shape no target can read (an extension or BCL <c>Deconstruct</c>, a
/// write through a collection indexer or an extension member) is reported as
/// MS0001 and becomes the unsupported placeholder.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The statements <paramref name="expression"/> lowers to when it is a
    /// deconstructing assignment whose value is discarded; <c>null</c> for
    /// any other expression.
    /// </summary>
    internal IReadOnlyList<IrStatement>? TryLowerDeconstruction(ExpressionSyntax expression)
    {
        if (
            expression
                is not AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                } assignment
            || SlotsOfTarget(assignment.Left) is not { } slots
        )
            return null;

        return new DeconstructionLowering(this, assignment).Lower(
            slots,
            _semantic.GetDeconstructionInfo(assignment),
            assignment.Right
        );
    }

    /// <summary>
    /// The statements that take apart the element of a deconstructing
    /// <c>foreach</c>, read from <paramref name="element"/>; <c>null</c> when
    /// the loop variable is not a deconstruction.
    /// </summary>
    internal IReadOnlyList<IrStatement>? LowerLoopDeconstruction(
        ForEachVariableStatementSyntax loop,
        IrExpression element
    )
    {
        if (SlotsOfTarget(loop.Variable) is not { } slots)
            return null;

        var elementType = _semantic.GetForEachStatementInfo(loop).ElementType;

        return new DeconstructionLowering(this, loop).LowerValue(
            slots,
            _semantic.GetDeconstructionInfo(loop),
            element,
            elementType is null ? null : IrTypeRefMapper.Map(elementType, _originResolver, _target)
        );
    }

    /// <summary>A fresh name for a temporary a deconstruction binds.</summary>
    private string NextDeconstructionTempName() => _temporaries.Next("$part");

    /// <summary>A fresh name for the element a deconstructing <c>foreach</c> iterates.</summary>
    internal string NextLoopElementName() => _temporaries.Next("$item");

    /// <summary>
    /// The slots of a deconstruction's left side: <c>var (a, (b, _))</c> or a
    /// tuple of declarations, discards, nested tuples and existing variables
    /// (<c>(x, var y, (z, _))</c>); <c>null</c> for any other target.
    /// </summary>
    private IReadOnlyList<DeconstructionSlot>? SlotsOfTarget(ExpressionSyntax target) =>
        target switch
        {
            DeclarationExpressionSyntax
            {
                Designation: ParenthesizedVariableDesignationSyntax designation
            } => SlotsOfDesignation(designation),
            TupleExpressionSyntax tuple => tuple
                .Arguments.Select(argument => SlotOfExpression(argument.Expression))
                .ToList(),
            _ => null,
        };

    private IReadOnlyList<DeconstructionSlot> SlotsOfDesignation(
        ParenthesizedVariableDesignationSyntax designation
    ) => designation.Variables.Select(SlotOfDesignation).ToList();

    private DeconstructionSlot SlotOfDesignation(VariableDesignationSyntax designation) =>
        designation switch
        {
            SingleVariableDesignationSyntax single => DeclaredSlotOf(single),
            ParenthesizedVariableDesignationSyntax nested => new NestedSlot(
                SlotsOfDesignation(nested)
            ),
            _ => new DiscardSlot(),
        };

    private DeclaredSlot DeclaredSlotOf(SingleVariableDesignationSyntax designation)
    {
        var local = _semantic.GetDeclaredSymbol(designation) as ILocalSymbol;

        return new DeclaredSlot(
            LocalIrNameOf(local, designation.Identifier.ValueText),
            local is null ? null : IrTypeRefMapper.Map(local.Type, _originResolver, _target)
        );
    }

    private DeconstructionSlot SlotOfExpression(ExpressionSyntax expression) =>
        expression switch
        {
            DeclarationExpressionSyntax declaration => SlotOfDesignation(declaration.Designation),
            TupleExpressionSyntax tuple => new NestedSlot(
                tuple.Arguments.Select(argument => SlotOfExpression(argument.Expression)).ToList()
            ),
            _ when _semantic.GetSymbolInfo(expression).Symbol is IDiscardSymbol =>
                new DiscardSlot(),
            _ => new StoredSlot(expression),
        };

    /// <summary>
    /// How the value <paramref name="info"/> describes is taken apart, or
    /// <c>null</c> (reported) when no target can read its parts.
    /// </summary>
    private DeconstructionShape? ShapeOf(DeconstructionInfo info, SyntaxNode site)
    {
        if (info.Method is not { } deconstruct)
            return new PositionalShape();

        if (LowersToArrayTuple(DeconstructedTypeOf(deconstruct)))
            return new PositionalShape();

        if (deconstruct.IsImplicitlyDeclared)
            return RecordMembersShape(deconstruct, site);

        if (CanCallDeconstruct(deconstruct))
            return new MethodShape(deconstruct);

        ReportUnsupportedFeature(
            site,
            $"Deconstruction through '{deconstruct.ContainingType.Name}.Deconstruct' is not "
                + "supported by the transpiler: only an instance 'Deconstruct' declared by a "
                + "transpiled type can be called."
        );

        return null;
    }

    private static ITypeSymbol DeconstructedTypeOf(IMethodSymbol deconstruct) =>
        deconstruct.ReducedFrom?.Parameters[0].Type
        ?? (deconstruct.IsExtensionMethod ? deconstruct.Parameters[0].Type : null)
        ?? deconstruct.ContainingType;

    private DeconstructionShape? RecordMembersShape(IMethodSymbol deconstruct, SyntaxNode site)
    {
        var members = deconstruct
            .Parameters.Select(p => ToPositionalElementMember(deconstruct.ContainingType, p.Name))
            .ToList();

        if (members.All(member => member is not null))
            return new MembersShape(members!);

        ReportUnsupportedFeature(
            site,
            $"Deconstruction of '{deconstruct.ContainingType.Name}' is not supported: some of its "
                + "outputs cannot be mapped to members."
        );

        return null;
    }

    /// <summary>
    /// Whether the deconstruction can call <paramref name="deconstruct"/>:
    /// an instance method of a type the toolchain lowers, whose emitted form
    /// returns its <c>out</c> values (see <see cref="IrOutArgumentCall"/>).
    /// </summary>
    private bool CanCallDeconstruct(IMethodSymbol deconstruct) =>
        deconstruct is { MethodKind: MethodKind.Ordinary, IsStatic: false }
        && ReturnsOutValues(deconstruct)
        && !IsOutsideTranspilation(deconstruct);

    /// <summary>
    /// <c>receiver.Deconstruct(out _, out _)</c> as the multi-value call whose
    /// values are the <c>out</c> arguments, in parameter order.
    /// </summary>
    private IrOutArgumentCall DeconstructCall(IrExpression receiver, IMethodSymbol deconstruct)
    {
        var origin = BuildOrigin(deconstruct);

        var overload = DispatchedMethodCalls.RefOf(deconstruct, _context) is { } reference
            ? MarkSynthesized(new IrSelectedOverload(reference, EvaluationOrder: null), deconstruct)
            : null;

        var call = new IrCallExpression(
            new IrMemberAccess(receiver, deconstruct.Name, origin) { Overload = overload },
            [],
            MethodTypeArgumentsOf(deconstruct),
            origin
        )
        {
            Overload = overload,
        };

        var outArguments = deconstruct
            .Parameters.Select(
                (parameter, ordinal) =>
                    new IrOutArgument(
                        ordinal,
                        parameter.Name,
                        new IrOutDiscard(),
                        DefaultOrMarker(parameter.Type)
                    )
            )
            .ToList();

        return new IrOutArgumentCall(call, outArguments, ResultType: null);
    }

    /// <summary>
    /// The existing variable <paramref name="target"/> writes, as an IR place,
    /// or <c>null</c> (reported) when the write needs a lowering of its own
    /// that a deconstruction does not run (a collection indexer, an extension
    /// member, a tuple element).
    /// </summary>
    private IrExpression? StorePlaceOf(ExpressionSyntax target)
    {
        var place = Extract(target);

        if (IsPlainStorePlace(place, target))
            return place;

        if (!IsUnsupportedPlaceholder(place))
            ReportUnsupportedFeature(
                target,
                $"Deconstruction into '{target}' is not supported by the transpiler: only "
                    + "locals, fields, properties and array elements can be written."
            );

        return null;
    }

    private bool IsPlainStorePlace(IrExpression place, ExpressionSyntax target) =>
        place switch
        {
            IrIdentifier => true,
            IrMemberAccess => _semantic.GetSymbolInfo(target).Symbol switch
            {
                IFieldSymbol field => !field.ContainingType.IsTupleType,
                IPropertySymbol property => TryResolveExtensionPropertyLoweringForSymbol(property)
                    is null,
                _ => false,
            },
            IrElementAccess => target is ElementAccessExpressionSyntax access
                && _semantic.GetTypeInfo(access.Expression).Type is IArrayTypeSymbol,
            _ => false,
        };

    /// <summary>Whether reading <paramref name="expression"/> again gives the same value.</summary>
    private static bool IsStableOperand(IrExpression expression) =>
        expression
            is IrIdentifier
                or IrThisExpression
                or IrBaseExpression
                or IrLiteral
                or IrTypeReference;

    private abstract record DeconstructionSlot;

    private sealed record DeclaredSlot(string Name, IrTypeRef? Type) : DeconstructionSlot;

    private sealed record DiscardSlot : DeconstructionSlot;

    private sealed record NestedSlot(IReadOnlyList<DeconstructionSlot> Slots) : DeconstructionSlot;

    private sealed record StoredSlot(ExpressionSyntax Target) : DeconstructionSlot;

    private abstract record DeconstructionShape;

    private sealed record PositionalShape : DeconstructionShape;

    private sealed record MembersShape(IReadOnlyList<IrPositionalElementMember> Members)
        : DeconstructionShape;

    private sealed record MethodShape(IMethodSymbol Method) : DeconstructionShape;
}
