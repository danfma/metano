using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// How an operation's result is brought back into its 64-bit domain: the
/// <see cref="IrIntegerOverflow"/> the extractor wraps it in.
/// </summary>
/// <param name="Kind">The 64-bit integer kind of the result.</param>
/// <param name="Mode">Wrapped (unchecked) or range-checked (checked).</param>
internal sealed record IntegerOverflowReduction(IrNumericKind Kind, IrOverflowMode Mode)
{
    public IrIntegerOverflow Reduce(IrExpression value) => new(value, Kind, Mode);
}
