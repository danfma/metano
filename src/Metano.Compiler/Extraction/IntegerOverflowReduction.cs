using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// How an operation's result is brought back into the domain of its integer
/// type: the <see cref="IrIntegerOverflow"/> the extractor wraps it in.
/// </summary>
/// <param name="Kind">The integer kind of the result.</param>
/// <param name="Mode">Wrapped (unchecked) or range-checked (checked).</param>
/// <param name="Operator">The reduction of the operator's own result, applied
/// before this one, when the stored value is a conversion of it that reads
/// it differently (a checked narrowing of the <c>int</c> result of
/// <c>&gt;&gt;&gt;</c>); <c>null</c> otherwise.</param>
internal sealed record IntegerOverflowReduction(
    IrNumericKind Kind,
    IrOverflowMode Mode,
    IntegerOverflowReduction? Operator = null
)
{
    public IrIntegerOverflow Reduce(IrExpression value) =>
        new(Operator?.Reduce(value) ?? value, Kind, Mode);

    /// <summary>The reduction to <paramref name="kind"/>: range-checked when <paramref name="isChecked"/>, wrapped otherwise.</summary>
    public static IntegerOverflowReduction Of(IrNumericKind kind, bool isChecked) =>
        new(kind, isChecked ? IrOverflowMode.Check : IrOverflowMode.Wrap);
}
