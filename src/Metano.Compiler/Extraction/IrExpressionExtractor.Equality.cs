using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>==</c> / <c>!=</c> forms whose C# meaning is value equality:
/// <list type="bullet">
///   <item>over a record (class or struct), the synthesized operator compares
///     by value, so it lowers to <see cref="IrBinaryOp.ValueEqual"/> /
///     <see cref="IrBinaryOp.ValueNotEqual"/>. A comparison with the
///     <c>null</c> literal stays a plain null check, which means the same,
///     and so does a <c>[Branded]</c> record over a primitive, whose value
///     <em>is</em> that primitive (ADR-0005);</item>
///   <item>over tuples, C# compares element-wise, short-circuiting left to
///     right after evaluating every operand element once, in source order.
///     It lowers to a conjunction (<c>==</c>) or disjunction (<c>!=</c>) of
///     element comparisons, each picking the operator C# binds for its
///     element types. When any operand value has side effects, every
///     non-literal one is let-bound first, so each runs exactly once and
///     before any comparison.</item>
/// </list>
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The value-equality lowering of <paramref name="bin"/>, or <c>null</c>
    /// when the comparison keeps the plain <see cref="IrBinaryOp.Equal"/> /
    /// <see cref="IrBinaryOp.NotEqual"/>.
    /// </summary>
    private IrExpression? TryExtractValueEquality(BinaryExpressionSyntax bin)
    {
        if (!bin.IsKind(SyntaxKind.EqualsExpression) && !bin.IsKind(SyntaxKind.NotEqualsExpression))
            return null;

        var negated = bin.IsKind(SyntaxKind.NotEqualsExpression);

        return _semantic.GetOperation(bin) switch
        {
            ITupleBinaryOperation tuple => ExtractTupleEquality(bin, tuple, negated),
            IBinaryOperation { OperatorMethod: { } method }
                when IsRecordValueComparison(method, bin) => new IrBinaryExpression(
                Extract(bin.Left),
                EqualityOperator(byValue: true, negated),
                Extract(bin.Right)
            ),
            _ => null,
        };
    }

    /// <summary>
    /// The record's synthesized <c>==</c> / <c>!=</c> (a record cannot
    /// declare its own), lifted or not, over a record that needs the value
    /// lowering, and not against the <c>null</c> literal.
    /// </summary>
    private bool IsRecordValueComparison(IMethodSymbol method, BinaryExpressionSyntax bin) =>
        IsSynthesizedEqualityOperator(method)
        && NeedsValueEqualityLowering(method.ContainingType)
        && !IsNullLiteral(bin.Left)
        && !IsNullLiteral(bin.Right);

    private static bool IsSynthesizedEqualityOperator(IMethodSymbol method) =>
        method.MethodKind is MethodKind.UserDefinedOperator
        && method.Name
            is WellKnownMemberNames.EqualityOperatorName
                or WellKnownMemberNames.InequalityOperatorName;

    /// <summary>
    /// Whether <c>==</c> over a value of <paramref name="type"/> means value
    /// equality the plain operator would not give: a record the toolchain
    /// emits (so the value carries its synthesized <c>equals</c>), except a
    /// <c>[Branded]</c> one over a primitive, which lowers to that primitive.
    /// An <c>[External]</c> or <c>[Import]</c> record is a foreign class with
    /// no known <c>equals</c>, so it keeps the plain operator.
    /// </summary>
    private bool NeedsValueEqualityLowering(ITypeSymbol? type) =>
        type is not null
        && UnwrapNullable(type) is INamedTypeSymbol { IsRecord: true } record
        && SymbolHelper.IsToolchainDeclared(record, _semantic.Compilation.Assembly)
        && !IsPrimitiveBackedBranded(record);

    private static bool IsPrimitiveBackedBranded(INamedTypeSymbol record) =>
        SymbolHelper.HasBranded(record)
        && SymbolHelper.GetBrandedValueType(record.OriginalDefinition) is { } valueType
        && IrTypeRefMapper.Map(valueType).IsBrandablePrimitive();

    private bool IsNullLiteral(ExpressionSyntax expression) =>
        _semantic.GetConstantValue(expression) is { HasValue: true, Value: null };

    private static IrBinaryOp EqualityOperator(bool byValue, bool negated) =>
        (byValue, negated) switch
        {
            (true, false) => IrBinaryOp.ValueEqual,
            (true, true) => IrBinaryOp.ValueNotEqual,
            (false, false) => IrBinaryOp.Equal,
            (false, true) => IrBinaryOp.NotEqual,
        };

    private IrExpression ExtractTupleEquality(
        BinaryExpressionSyntax bin,
        ITupleBinaryOperation tuple,
        bool negated
    )
    {
        if (IsLiftedTupleOperand(tuple.LeftOperand) || IsLiftedTupleOperand(tuple.RightOperand))
            return ReportUnsupported(bin, "nullable tuple equality");

        var left = TupleOperandOf(bin.Left);
        var right = TupleOperandOf(bin.Right);

        if (ExceedsFlatArity(left) || ExceedsFlatArity(right))
        {
            return ReportUnsupported(
                bin,
                $"tuple equality over more than {SymbolHelper.MaxFlatTupleArity} elements"
            );
        }

        if (left.AllLeavesSideEffectFree() && right.AllLeavesSideEffectFree())
            return CompareTupleOperands(left, right, negated);

        var bindings = new List<(string Name, IrExpression Value)>();
        var boundLeft = BindLeaves(left, bindings);
        var boundRight = BindLeaves(right, bindings);

        return WrapInLetBindings(CompareTupleOperands(boundLeft, boundRight, negated), bindings);
    }

    private static bool IsLiftedTupleOperand(IOperation operand) =>
        operand.Type
            is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool ExceedsFlatArity(TupleOperand operand) =>
        operand.Arity > SymbolHelper.MaxFlatTupleArity;

    /// <summary>
    /// A tuple literal keeps its elements apart, so each one is evaluated
    /// (and compared) on its own; any other operand is a single value whose
    /// elements are read by position.
    /// </summary>
    private TupleOperand TupleOperandOf(ExpressionSyntax syntax)
    {
        var unwrapped = Unparenthesized(syntax);
        var type = _semantic.GetTypeInfo(unwrapped).Type;

        if (unwrapped is not TupleExpressionSyntax literal)
            return TupleOperand.Single(type, Extract(unwrapped));

        var elements = literal.Arguments.Select(a => TupleOperandOf(a.Expression)).ToList();

        return TupleOperand.Literal(type, elements);
    }

    /// <summary>
    /// Rebuilds <paramref name="operand"/> with every non-literal leaf value
    /// replaced by a fresh temporary, recording the bindings in evaluation
    /// order (left to right, depth first). The temporaries are scoped to the
    /// let chain <see cref="WrapInLetBindings"/> builds around the comparison.
    /// </summary>
    private TupleOperand BindLeaves(
        TupleOperand operand,
        List<(string Name, IrExpression Value)> bindings
    )
    {
        if (operand.Elements is { } elements)
            return operand with
            {
                Elements = elements.Select(e => BindLeaves(e, bindings)).ToList(),
            };

        if (operand.SingleValue is IrLiteral)
            return operand;

        var name = NextTupleOperandTempName();
        bindings.Add((name, operand.SingleValue));

        return operand with
        {
            Value = new IrIdentifier(name),
        };
    }

    private static IrExpression WrapInLetBindings(
        IrExpression body,
        IReadOnlyList<(string Name, IrExpression Value)> bindings
    ) =>
        bindings
            .Reverse()
            .Aggregate(
                body,
                (inner, binding) => new IrLetExpression(binding.Name, binding.Value, inner)
            );

    private IrExpression CompareTupleOperands(TupleOperand left, TupleOperand right, bool negated)
    {
        if (!left.IsTuple || !right.IsTuple)
            return CompareTupleElements(left, right, negated);

        var combinator = negated ? IrBinaryOp.LogicalOr : IrBinaryOp.LogicalAnd;

        return Enumerable
            .Range(0, left.Arity)
            .Select(i => CompareTupleOperands(left.ElementAt(i), right.ElementAt(i), negated))
            .Aggregate((acc, next) => new IrBinaryExpression(acc, combinator, next));
    }

    /// <summary>
    /// One element pair, with the operator C# binds for it: decimal equality
    /// when a side is a <c>decimal</c> (the same <c>eq</c> call a scalar
    /// decimal <c>==</c> lowers to, on the decimal side so a converted
    /// integer operand works), the record one when a side is a record that
    /// needs the value lowering and neither is the <c>null</c> literal, the
    /// plain one otherwise.
    /// </summary>
    private IrExpression CompareTupleElements(TupleOperand left, TupleOperand right, bool negated)
    {
        var decimalMethod = MapDecimalBinaryMethod(EqualityOperator(byValue: false, negated))!;

        if (IsDecimal(left.Type))
            return BuildDecimalBinaryCall(left.SingleValue, right.SingleValue, decimalMethod);

        if (IsDecimal(right.Type))
            return BuildDecimalBinaryCall(right.SingleValue, left.SingleValue, decimalMethod);

        return new IrBinaryExpression(
            left.SingleValue,
            EqualityOperator(NeedsValueEquality(left, right), negated),
            right.SingleValue
        );
    }

    private static bool IsDecimal(ITypeSymbol? type) =>
        type?.SpecialType == SpecialType.System_Decimal;

    private bool NeedsValueEquality(TupleOperand left, TupleOperand right) =>
        (NeedsValueEqualityLowering(left.Type) || NeedsValueEqualityLowering(right.Type))
        && !left.IsNullLiteral
        && !right.IsNullLiteral;

    /// <summary>
    /// One side of a tuple comparison: a literal's separate
    /// <see cref="Elements"/> (<see cref="Literal"/>), or one
    /// <see cref="Value"/> of <see cref="Type"/> — a whole tuple or a scalar
    /// element (<see cref="Single"/>). Exactly one of the two is set.
    /// </summary>
    private sealed record TupleOperand(
        ITypeSymbol? Type,
        IrExpression? Value,
        IReadOnlyList<TupleOperand>? Elements
    )
    {
        public static TupleOperand Literal(
            ITypeSymbol? type,
            IReadOnlyList<TupleOperand> elements
        ) => new(type, Value: null, elements);

        public static TupleOperand Single(ITypeSymbol? type, IrExpression value) =>
            new(type, value, Elements: null);

        public IrExpression SingleValue =>
            Value
            ?? throw new InvalidOperationException("A tuple literal operand has no single value.");

        public bool IsTuple =>
            Elements is not null || Type is INamedTypeSymbol { IsTupleType: true };

        public int Arity =>
            Elements?.Count ?? (Type as INamedTypeSymbol)?.TupleElements.Length ?? 0;

        public bool IsNullLiteral => Value is IrLiteral { Kind: IrLiteralKind.Null };

        public bool AllLeavesSideEffectFree() =>
            Elements?.All(e => e.AllLeavesSideEffectFree())
            ?? IrEvaluationTraits.IsSideEffectFree(SingleValue);

        /// <summary>
        /// The element at <paramref name="index"/>: a literal's own element, or
        /// a positional read of the value — the same tuple-element access a
        /// source <c>t.ItemN</c> produces, so each backend reads it its own
        /// way.
        /// </summary>
        public TupleOperand ElementAt(int index)
        {
            if (Elements is { } elements)
                return elements[index];

            var tupleType = (INamedTypeSymbol)Type!;
            var itemName = SymbolHelper.TupleItemName(index);
            var origin = new IrMemberOrigin(
                tupleType.GetStableFullName(),
                itemName,
                IsTupleElement: true,
                TupleIndex: index
            );

            return Single(
                tupleType.TupleElements[index].Type,
                new IrMemberAccess(SingleValue, itemName, origin)
            );
        }
    }
}
