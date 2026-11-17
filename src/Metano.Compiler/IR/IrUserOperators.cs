namespace Metano.Compiler.IR;

/// <summary>
/// The user-defined operator or conversion C# bound at an application
/// (baseline FR-077, FR-079): neutral facts from the Roslyn symbol, from which
/// each target names what it calls.
/// </summary>
/// <param name="DeclaringType">The type that declares the operator, as a value
/// reference (its import included).</param>
/// <param name="MetadataName">The operator's C# metadata name
/// (<c>op_Addition</c>, <c>op_UnaryNegation</c>, <c>op_Implicit</c>).</param>
/// <param name="Overload">The overload of a dispatched operator group Roslyn
/// resolved; <c>null</c> for a single operator and for a conversion.</param>
/// <param name="Pin">The <c>[Name]</c> of a single operator for the emitting
/// target, which renames it whole; <c>null</c> when none.</param>
/// <param name="ConversionName">The conversion's name candidate
/// (<see cref="IrConversionName"/>); <c>null</c> for an operator.</param>
public sealed record IrUserOperator(
    IrTypeReference DeclaringType,
    string MetadataName,
    IrOverloadRef? Overload,
    string? Pin,
    IrConversionName? ConversionName
)
{
    private const string MetadataPrefix = "op_";

    /// <summary>
    /// The operator kind the metadata name spells (<c>Addition</c>,
    /// <c>CheckedAddition</c>), as <see cref="IrMethodSemantics.OperatorKind"/>
    /// spells it at the declaration.
    /// </summary>
    public string Kind =>
        MetadataName.StartsWith(MetadataPrefix, StringComparison.Ordinal)
            ? MetadataName[MetadataPrefix.Length..]
            : MetadataName;
}

/// <summary>
/// An application of a user-defined operator (baseline FR-077): a binary,
/// unary, relational or equality operator whose C# binding is an
/// <c>operator</c> method of a transpiled type. Never an
/// <see cref="IrBinaryExpression"/> or an <see cref="IrUnaryExpression"/>, so
/// no lowering of the built-in operators rewrites it.
/// </summary>
/// <param name="Operator">The operator C# bound.</param>
/// <param name="Operands">Roslyn's bound operands, left to right, each with its
/// conversion to the operator's parameter.</param>
/// <param name="BinaryOp">The source operator of a binary application.</param>
/// <param name="UnaryOp">The source operator of a unary application.</param>
/// <param name="IsLifted">An <c>==</c> / <c>!=</c> lifted over
/// <c>Nullable&lt;T&gt;</c>: <c>true</c> when both operands are <c>null</c>,
/// <c>false</c> when one is, the operator's result otherwise. The other lifted
/// operators are lowered around the application by the core.</param>
public sealed record IrUserOperatorApplication(
    IrUserOperator Operator,
    IReadOnlyList<IrExpression> Operands,
    IrBinaryOp? BinaryOp = null,
    IrUnaryOp? UnaryOp = null,
    bool IsLifted = false
) : IrExpression;

/// <summary>
/// A compound assignment (<c>x += y</c>) or an increment (<c>x++</c>) whose
/// operator is user-defined (baseline FR-077).
/// </summary>
/// <param name="SourceForm">The store as the source writes it: the compound
/// <see cref="IrBinaryExpression"/> (<c>x += y</c>) or the
/// <see cref="IrUnaryExpression"/> increment, over the extracted target and
/// operand.</param>
/// <param name="ReadModifyWrite">The store that runs the operator:
/// <c>x = T.op(x, y)</c> with the target's receiver and index evaluated once,
/// and, for a used postfix increment, the value read before the store.</param>
public sealed record IrUserOperatorStore(IrExpression SourceForm, IrExpression ReadModifyWrite)
    : IrExpression;

/// <summary>
/// An application of a user-defined conversion operator (baseline FR-079),
/// implicit or explicit, of a transpiled type.
/// </summary>
/// <param name="Operator">The conversion C# bound.</param>
/// <param name="Operand">The converted value, already converted to the
/// operator's parameter (<c>Before</c>, a numeric conversion node where one is
/// needed).</param>
/// <param name="After">The numeric conversion of the operator's result to the
/// target type; <c>null</c> when none is needed.</param>
/// <param name="IsLifted">A <c>null</c> operand yields <c>null</c> without
/// calling the operator (a lifted conversion, or an explicit one from a
/// nullable value to a class); the operand is evaluated once.</param>
/// <param name="SourceForm">The conversion as the source writes it, without
/// the operator: the operand of an implicit conversion, the cast of an
/// explicit one.</param>
public sealed record IrUserConversion(
    IrUserOperator Operator,
    IrExpression Operand,
    IrNumericStep? After,
    bool IsLifted,
    IrExpression SourceForm
) : IrExpression;

/// <summary>
/// A numeric conversion applied around a user-defined conversion (baseline
/// FR-079): the endpoints of the <see cref="IrNumericConversion"/> it stands
/// for, without an operand.
/// </summary>
public sealed record IrNumericStep(IrNumericEndpoint From, IrNumericEndpoint To, bool IsChecked);
