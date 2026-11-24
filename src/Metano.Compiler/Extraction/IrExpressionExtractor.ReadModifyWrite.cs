using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Compound assignments and increments with no native target operator,
/// lowered to <c>target = compute(target)</c> with the target's receiver and
/// index bound once: integer and boolean compound operators, lifted
/// compound operators, and nullable <c>++</c> / <c>--</c>.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="op"/> narrowed to its integer-division or boolean-logic
    /// form when <paramref name="operation"/> binds to one; unchanged
    /// otherwise.
    /// </summary>
    private IrBinaryOp NarrowBuiltinOperator(IrBinaryOp op, ExpressionSyntax operation) =>
        NarrowToBooleanLogic(NarrowToIntegerDivision(op, operation), operation);

    /// <summary>
    /// Builds <paramref name="target"/> <c>op=</c> <c>right</c>. An integer
    /// <c>/=</c>, a boolean <c>&amp;=</c> / <c>|=</c> / <c>^=</c>, a
    /// 64-bit compound operator that wraps,
    /// checks, divides or shifts, and a compound operator on an integer of
    /// 32 bits or fewer whose stored value is narrowed, wrapped or checked
    /// (FR-075–FR-077) have no compound operator in the targets
    /// that keeps C# semantics, so each becomes the read-modify-write
    /// <c>target = target op right</c> (see <see cref="BuildReadModifyWrite"/>)
    /// storing the value <see cref="CompoundValue"/> computes.
    /// </summary>
    private IrExpression BuildCompoundAssignment(
        IrExpression target,
        AssignmentExpressionSyntax assign
    )
    {
        var right = Extract(assign.Right);

        if (MapCompoundAssignmentOperator(assign) is { } charOp && IsCharStoreTarget(assign.Left))
            return BuildCharCompoundAssignment(target, assign, charOp, right);

        if (MapCompoundAssignmentOperator(assign) is { } op && RequiresReadModifyWrite(op, assign))
        {
            if (NativeCompoundShiftKindOf(target, op, assign) is { } kind)
                return new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right, kind);

            return BuildReadModifyWrite(target, assign, readTarget =>
                CompoundValue(assign, readTarget, op, right)
            );
        }

        return new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right);
    }

    private bool RequiresReadModifyWrite(IrBinaryOp op, AssignmentExpressionSyntax assign) =>
        HasNoTargetCompoundOperator(op)
        || NeedsOverflowReduction(assign)
        || NeedsSixtyFourBitReadModifyWrite(op, assign)
        || NeedsLogicalShiftReadModifyWrite(op, assign)
        || IsDecimalCompound(op, assign);

    private static bool HasNoTargetCompoundOperator(IrBinaryOp op) =>
        op
            is IrBinaryOp.IntegerDivide
                or IrBinaryOp.BooleanAnd
                or IrBinaryOp.BooleanOr
                or IrBinaryOp.BooleanXor;

    private bool NeedsOverflowReduction(AssignmentExpressionSyntax assign) =>
        ReductionOf(_semantic.GetOperation(assign)) is not null;

    /// <summary>
    /// The value <c>target op= right</c> stores: the binary form over the
    /// current value, carrying its enum result type before receiver binding
    /// and reduced as the compound operation needs (FR-024), or
    /// the method call of a <c>decimal</c> compound operator.
    /// </summary>
    private IrExpression CompoundValue(
        AssignmentExpressionSyntax assign,
        IrExpression current,
        IrBinaryOp op,
        IrExpression right
    ) =>
        IsDecimalCompound(op, assign)
            ? BuildDecimalBinaryCall(current, right, MapDecimalBinaryMethod(op)!)
            : ReduceOverflow(
                _semantic.GetOperation(assign),
                WithEnumResultType(
                    assign,
                    BuildNumericBinary(
                        assign,
                        op,
                        current,
                        new OperatorOperand(right, MayBeNull: false, assign.Right)
                    )
                )
            );

    /// <summary>
    /// The value an extension property's <c>target op= right</c> stores: the
    /// same compound value a field stores, lifted when the property's type is
    /// <c>Nullable&lt;T&gt;</c> (#497) — there the getter's value takes the
    /// null guard a nullable field takes, since the JS operator would coerce
    /// <c>null</c> into a number (or throw on <c>null + 1n</c>).
    /// </summary>
    private IrExpression ExtensionCompoundValue(
        AssignmentExpressionSyntax assign,
        IPropertySymbol property,
        IrExpression current,
        IrBinaryOp op,
        IrExpression right
    ) =>
        StoredUnderNullGuard(
            property,
            current,
            WithEnumResultType(
                assign,
                CompoundValue(assign, GuardedRead(property, current), op, right)
            )
        );

    /// <summary>
    /// The value an extension property's <c>++</c> / <c>--</c> stores: the
    /// stepped current value, lifted when the property's type is
    /// <c>Nullable&lt;T&gt;</c> (#497), like <see cref="ExtensionCompoundValue"/>,
    /// and stepped through its method when it is a <c>decimal</c> the target
    /// computes with by methods (#695).
    /// </summary>
    private IrExpression ExtensionIncrementValue(
        ExpressionSyntax node,
        IPropertySymbol property,
        IrExpression current,
        IrBinaryOp stepOperator,
        IrExpression one
    ) =>
        IsDecimalComputedByMethods(property.Type)
            ? BuildDecimalBinaryCall(current, DecimalOne, MapDecimalBinaryMethod(stepOperator)!)
            : StoredUnderNullGuard(
                property,
                current,
                ReduceOverflow(
                    _semantic.GetOperation(node),
                    WithEnumResultType(
                        node,
                        new IrBinaryExpression(GuardedRead(property, current), stepOperator, one)
                    )
                )
            );

    /// <summary>
    /// The operand a lifted extension-property read-modify-write applies its
    /// operator to: the null-asserted getter call. TypeScript narrows a
    /// repeated property read after a <c>== null</c> test, but not a repeated
    /// function result, so without the assertion the stored form keeps a
    /// <c>bigint | null</c> operand and fails <c>tsc --strict</c> (#497).
    /// </summary>
    private static IrExpression GuardedRead(IPropertySymbol property, IrExpression current) =>
        property.Type.IsNullableValueType()
            ? new IrUnaryExpression(IrUnaryOp.NonNullAssertion, current, IsPrefix: false)
            : current;

    /// <summary>
    /// <paramref name="stored"/> under the lifted null guard a
    /// <c>Nullable&lt;T&gt;</c> extension property's read-modify-write keeps
    /// its value behind (#497): <c>current == null ? null : stored</c>, the
    /// shape a nullable field stores. The getter call is repeated, as a
    /// property read is assumed side-effect free
    /// (see <see cref="IrEvaluationTraits.IsReusableReceiver"/>).
    /// </summary>
    private static IrExpression StoredUnderNullGuard(
        IPropertySymbol property,
        IrExpression current,
        IrExpression stored
    ) =>
        property.Type.IsNullableValueType()
            ? new IrConditionalExpression(IsNull(current), NullLiteral, stored)
            : stored;

    /// <summary>
    /// <c>target = compute(target)</c>, reading the target twice (see
    /// <see cref="BindTargetOnce"/>).
    /// </summary>
    private IrExpression BuildReadModifyWrite(
        IrExpression target,
        ExpressionSyntax operation,
        Func<IrExpression, IrExpression> compute
    ) => BindTargetOnce(target, operation, readTarget => WriteInPlace(readTarget, compute));

    /// <summary>
    /// Builds an expression that reads and writes <paramref name="target"/>
    /// through the reusable form <paramref name="build"/> receives. The
    /// receiver of a member access and the receiver and index of an element
    /// access are evaluated once, in source order, as C# does for a compound
    /// assignment or an increment: a reusable one is repeated, anything else
    /// is let-bound first. Any other assignable shape is reported as
    /// unsupported rather than evaluated twice, and so is a dictionary
    /// entry: the read of a missing key would yield <c>undefined</c> where C#
    /// throws <c>KeyNotFoundException</c>.
    /// </summary>
    private IrExpression BindTargetOnce(
        IrExpression target,
        ExpressionSyntax operation,
        Func<IrExpression, IrExpression> build
    ) =>
        target switch
        {
            _ when IsDictionaryEntry(target) => ReportDictionaryEntryStore(operation),
            _ when IrEvaluationTraits.IsReusableReceiver(target) => build(target),
            IrMemberAccess member => BindReceiverOnce(member.Target, receiver =>
                build(member with { Target = receiver })
            ),
            IrElementAccess element => BindElementOnce(element, build),
            _ => ReportUnsupported(
                operation,
                operation.Kind().ToString(),
                $"'{operation}' is not supported: its target cannot be read and written "
                    + "without evaluating it twice."
            ),
        };

    /// <summary>
    /// Whether <paramref name="target"/> is a dictionary entry
    /// (<c>dict[key]</c>), which the 2.x lowering reads as a map entry.
    /// </summary>
    private static bool IsDictionaryEntry(IrExpression target) =>
        target is IrElementAccess element
        && element.TargetType.WithLegacyCollection() is IrMapTypeRef;

    private IrExpression BindElementOnce(
        IrElementAccess element,
        Func<IrExpression, IrExpression> build
    ) =>
        BindReceiverOnce(element.Target, receiver =>
            BindValueOnce(element.Index, index =>
                build(element with { Target = receiver, Index = index })
            )
        );

    private static IrBinaryExpression WriteInPlace(
        IrExpression target,
        Func<IrExpression, IrExpression> compute
    ) => new(target, IrBinaryOp.Assign, compute(target));

    /// <summary>
    /// A reduced store on a dictionary entry: the read-modify-write cannot
    /// read a missing key as C# does (it throws <c>KeyNotFoundException</c>),
    /// so it reports MS0001. The store FR-074 adds, a non-lifted unchecked
    /// <c>int</c> / <c>uint</c> <c>+=</c>, <c>-=</c>, <c>*=</c>, <c>++</c> or
    /// <c>--</c> that now wraps, is an error: before FR-074 it emitted the
    /// native operator on <c>map.get(key)</c>, which <c>tsc</c> rejected, so
    /// the build keeps failing. The stores reported before FR-074 (checked
    /// forms, the types narrower than <c>int</c>, shifts, <c>uint</c> bitwise
    /// reads) keep their warning.
    /// </summary>
    private IrUnsupportedExpression ReportDictionaryEntryStore(ExpressionSyntax operation)
    {
        var severity = StoresAnInt32ArithmeticWrap(operation)
            ? MetanoDiagnosticSeverity.Error
            : MetanoDiagnosticSeverity.Warning;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                severity,
                DiagnosticCodes.UnsupportedFeature,
                $"'{operation}' on a dictionary entry is not supported.",
                operation.GetLocation()
            )
        );

        return new IrUnsupportedExpression(operation.Kind().ToString());
    }

    /// <summary>
    /// Whether <paramref name="operation"/> stores the FR-074 wrap of
    /// <c>int</c> / <c>uint</c> arithmetic: an increment, a decrement or an
    /// arithmetic compound assignment whose stored value wraps to 32 bits.
    /// A lifted store (on an <c>int?</c> / <c>uint?</c> entry) is not one:
    /// before FR-074 it emitted a throwing placeholder that compiled, so it
    /// keeps the warning.
    /// </summary>
    private bool StoresAnInt32ArithmeticWrap(ExpressionSyntax operation)
    {
        var store = _semantic.GetOperation(operation);

        var isArithmeticStore =
            store
            is IIncrementOrDecrementOperation { IsLifted: false }
                or ICompoundAssignmentOperation
                {
                    IsLifted: false,
                    OperatorKind: BinaryOperatorKind.Add
                        or BinaryOperatorKind.Subtract
                        or BinaryOperatorKind.Multiply,
                };

        return isArithmeticStore
            && ReductionOf(store)
                is {
                    Mode: IrOverflowMode.Wrap,
                    Kind: IrNumericKind.Int32 or IrNumericKind.UInt32,
                    Operator: null,
                };
    }
}
