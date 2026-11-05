namespace Metano.Compiler.IR;

/// <summary>
/// The name the core's overload naming policy computed for one overload of a
/// dispatched group (spec 013 FR-001). A target derives its
/// candidate member names from <see cref="Suffix"/> (<c>add</c> +
/// <c>XY</c>, <c>__add</c> + <c>MoneyInt32</c>) and claims them against its own
/// member tables, so the emitted spelling may differ (DECIDED-18).
/// </summary>
/// <param name="Suffix">The overload's suffix: its parameter names
/// (<c>WidthHeight</c>), its parameter type spellings (<c>Int32</c>,
/// <c>NumericsDecimal</c>), <c>Default</c> for a parameterless overload, or a
/// spelling with a positional marker (<c>GlobalNDecimal$1</c>) when the climb
/// reached a dead end (FR-024).</param>
public sealed record IrOverloadName(string Suffix);
