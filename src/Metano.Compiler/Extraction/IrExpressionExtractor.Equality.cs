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
        && HasSynthesizedValueEquality(method.ContainingType)
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
    /// <para>
    /// A <c>[PlainObject]</c> record lowers to an object literal with no
    /// <c>equals</c>, so the runtime compares it by shape: its own keys and
    /// their values. That is weaker than C#'s record equality in two ways —
    /// a missing optional key and a <c>null</c> one differ (the key counts
    /// differ), and two record types with the same shape compare equal (no
    /// <c>EqualityContract</c>).
    /// </para>
    /// </summary>
    private bool HasSynthesizedValueEquality(ITypeSymbol? type) =>
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

        if (left is null || right is null)
        {
            return ReportUnsupported(
                bin,
                "tuple equality against default",
                $"Expression '{bin}' compares a tuple with a default value whose element "
                    + "defaults the transpiler cannot spell; it is not supported by the transpiler."
            );
        }

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
    /// (and compared) on its own; a <c>default</c> spells out each element's
    /// default value the same way; any other operand is a single value whose
    /// elements are read by position. <c>null</c> when a <c>default</c> has an
    /// element whose default value has no literal
    /// (<see cref="DefaultOperandOf"/>).
    /// </summary>
    private TupleOperand? TupleOperandOf(ExpressionSyntax syntax)
    {
        var unwrapped = Unparenthesized(syntax);
        var typeInfo = _semantic.GetTypeInfo(unwrapped);

        if (IsDefaultValueSyntax(unwrapped))
            return DefaultOperandOf(typeInfo.ConvertedType ?? typeInfo.Type);

        if (unwrapped is not TupleExpressionSyntax literal)
            return new ValueOperand(typeInfo.Type, Extract(unwrapped));

        var elements = literal.Arguments.Select(a => TupleOperandOf(a.Expression)).ToList();

        return elements.Contains(null)
            ? null
            : new LiteralOperand(typeInfo.Type, elements.OfType<TupleOperand>().ToList());
    }

    private static bool IsDefaultValueSyntax(ExpressionSyntax syntax) =>
        syntax is DefaultExpressionSyntax || syntax.IsKind(SyntaxKind.DefaultLiteralExpression);

    /// <summary>
    /// <c>default</c> of <paramref name="type"/> as an operand: a tuple's
    /// default is the tuple of its elements' defaults, so it compares element
    /// by element like a literal (reading positions off an
    /// <c>undefined</c> value would throw). A scalar default is its literal
    /// (<see cref="DefaultLiteralOf"/>); <c>null</c> when it has none.
    /// </summary>
    private static TupleOperand? DefaultOperandOf(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            var elements = tupleType.TupleElements.Select(e => DefaultOperandOf(e.Type)).ToList();

            return elements.Contains(null)
                ? null
                : new LiteralOperand(type, elements.OfType<TupleOperand>().ToList());
        }

        return DefaultLiteralOf(type) is { } literal ? new ValueOperand(type, literal) : null;
    }

    /// <summary>
    /// The literal C#'s <c>default</c> of <paramref name="type"/> is: <c>null</c>
    /// for a reference, nullable or type-parameter type (as a bare
    /// <c>default</c> extracts), <c>false</c>, or a numeric zero. <c>null</c>
    /// (no literal) for any other value type: a struct's default is a value
    /// the comparison cannot spell as a literal.
    /// </summary>
    private static IrLiteral? DefaultLiteralOf(ITypeSymbol? type) =>
        type switch
        {
            null => null,
            { IsReferenceType: true } or ITypeParameterSymbol => new IrLiteral(
                null,
                IrLiteralKind.Null
            ),
            _ when type.IsNullableValueType() => new IrLiteral(null, IrLiteralKind.Null),
            { SpecialType: SpecialType.System_Boolean } => new IrLiteral(
                false,
                IrLiteralKind.Boolean
            ),
            { SpecialType: SpecialType.System_Int64 or SpecialType.System_UInt64 } =>
                ClassifyNumeric(0L),
            {
                SpecialType: SpecialType.System_Int32
                    or SpecialType.System_UInt32
                    or SpecialType.System_Int16
                    or SpecialType.System_UInt16
                    or SpecialType.System_Byte
                    or SpecialType.System_SByte
            } => ClassifyNumeric(0),
            { SpecialType: SpecialType.System_Double or SpecialType.System_Single } =>
                ClassifyNumeric(0d),
            { SpecialType: SpecialType.System_Decimal } => ClassifyNumeric(0m),
            _ => null,
        };

    /// <summary>
    /// Rebuilds <paramref name="operand"/> with every non-literal leaf value
    /// replaced by a fresh temporary, recording the bindings in evaluation
    /// order (left to right, depth first). The temporaries are scoped to the
    /// let chain <see cref="WrapInLetBindings"/> builds around the comparison.
    /// </summary>
    private TupleOperand BindLeaves(
        TupleOperand operand,
        List<(string Name, IrExpression Value)> bindings
    ) =>
        operand switch
        {
            LiteralOperand literal => literal with
            {
                Elements = literal.Elements.Select(e => BindLeaves(e, bindings)).ToList(),
            },
            ValueOperand { Value: IrLiteral } => operand,
            ValueOperand value => value with { Value = BindToTemporary(value.Value, bindings) },
            _ => operand,
        };

    private IrIdentifier BindToTemporary(
        IrExpression value,
        List<(string Name, IrExpression Value)> bindings
    )
    {
        var name = NextTupleOperandTempName();
        bindings.Add((name, value));

        return new IrIdentifier(name);
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
            return CompareTupleElements(AsValue(left), AsValue(right), negated);

        var combinator = negated ? IrBinaryOp.LogicalOr : IrBinaryOp.LogicalAnd;

        return Enumerable
            .Range(0, left.Arity)
            .Select(i => CompareTupleOperands(left.ElementAt(i), right.ElementAt(i), negated))
            .Aggregate((acc, next) => new IrBinaryExpression(acc, combinator, next));
    }

    /// <summary>
    /// A non-tuple operand: always a single value, since a literal operand is
    /// a tuple and C# only compares a tuple with a tuple.
    /// </summary>
    private static ValueOperand AsValue(TupleOperand operand) =>
        operand as ValueOperand
        ?? throw new InvalidOperationException("A tuple literal operand has no single value.");

    /// <summary>
    /// One element pair, with the operator C# binds for it: decimal equality
    /// when a side is a <c>decimal</c> (the same <c>eq</c> call a scalar
    /// decimal <c>==</c> lowers to, on the decimal side so a converted
    /// integer operand works), the record one when a side is a record that
    /// needs the value lowering and neither is the <c>null</c> literal, the
    /// plain one otherwise.
    /// </summary>
    private IrExpression CompareTupleElements(ValueOperand left, ValueOperand right, bool negated)
    {
        if (IsDecimal(left.Type))
            return BuildDecimalEquality(left.Value, right.Value, negated);

        if (IsDecimal(right.Type))
            return BuildDecimalEquality(right.Value, left.Value, negated);

        return new IrBinaryExpression(
            left.Value,
            EqualityOperator(ComparesElementsByValue(left, right), negated),
            right.Value
        );
    }

    private static IrExpression BuildDecimalEquality(
        IrExpression decimalSide,
        IrExpression otherSide,
        bool negated
    ) =>
        BuildDecimalBinaryCall(
            decimalSide,
            otherSide,
            MapDecimalBinaryMethod(EqualityOperator(byValue: false, negated))!
        );

    private static bool IsDecimal(ITypeSymbol? type) =>
        type?.SpecialType == SpecialType.System_Decimal;

    private bool ComparesElementsByValue(ValueOperand left, ValueOperand right) =>
        (HasSynthesizedValueEquality(left.Type) || HasSynthesizedValueEquality(right.Type))
        && !left.IsNullLiteral
        && !right.IsNullLiteral;

    /// <summary>
    /// One side of a tuple comparison, of <see cref="Type"/>: a literal's
    /// separate elements (<see cref="LiteralOperand"/>), or one value — a
    /// whole tuple or a scalar element (<see cref="ValueOperand"/>).
    /// </summary>
    private abstract record TupleOperand(ITypeSymbol? Type)
    {
        public bool IsTuple =>
            this is LiteralOperand || Type is INamedTypeSymbol { IsTupleType: true };

        public abstract int Arity { get; }

        public abstract bool AllLeavesSideEffectFree();

        public abstract TupleOperand ElementAt(int index);
    }

    /// <summary>A tuple literal (or spelled-out <c>default</c>), element by element.</summary>
    private sealed record LiteralOperand(ITypeSymbol? Type, IReadOnlyList<TupleOperand> Elements)
        : TupleOperand(Type)
    {
        public override int Arity => Elements.Count;

        public override bool AllLeavesSideEffectFree() =>
            Elements.All(e => e.AllLeavesSideEffectFree());

        public override TupleOperand ElementAt(int index) => Elements[index];
    }

    /// <summary>One value: a whole tuple, or a scalar element.</summary>
    private sealed record ValueOperand(ITypeSymbol? Type, IrExpression Value) : TupleOperand(Type)
    {
        public override int Arity => (Type as INamedTypeSymbol)?.TupleElements.Length ?? 0;

        public bool IsNullLiteral => Value is IrLiteral { Kind: IrLiteralKind.Null };

        public override bool AllLeavesSideEffectFree() =>
            IrEvaluationTraits.IsSideEffectFree(Value);

        /// <summary>
        /// A positional read of the element at <paramref name="index"/> — the
        /// same tuple-element access a source <c>t.ItemN</c> produces, so each
        /// backend reads it its own way.
        /// </summary>
        public override TupleOperand ElementAt(int index)
        {
            var tupleType = (INamedTypeSymbol)Type!;
            var itemName = SymbolHelper.TupleItemName(index);

            var origin = new IrMemberOrigin(
                tupleType.GetStableFullName(),
                itemName,
                TupleElement: IrTupleElementKind.ValueTuple,
                TupleIndex: index
            );

            return new ValueOperand(
                tupleType.TupleElements[index].Type,
                new IrMemberAccess(Value, itemName, origin)
            );
        }
    }
}
