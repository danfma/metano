namespace Metano.Compiler.IR;

/// <summary>
/// Which way a user-defined conversion operator converts, seen from the type
/// that declares it.
/// </summary>
public enum IrConversionDirection
{
    /// <summary>Out of the declaring type (<c>explicit operator int(Money m)</c>).</summary>
    To,

    /// <summary>Into the declaring type (<c>implicit operator Money(int c)</c>).</summary>
    From,
}

/// <summary>
/// Which kind of user-defined conversion operator C# declares. C# forbids an
/// implicit and an explicit conversion between the same two types (CS0557),
/// so the kind never tells two conversions apart; it is part of the name so a
/// reader sees it (maintainer decision D1).
/// </summary>
public enum IrConversionKind
{
    /// <summary><c>implicit operator</c>.</summary>
    Implicit,

    /// <summary><c>explicit operator</c>.</summary>
    Explicit,

    /// <summary><c>explicit operator checked</c>, which C# binds in a checked context.</summary>
    Checked,
}

/// <summary>
/// The target-neutral name candidate of one user-defined conversion operator
/// (baseline FR-078, #600): C# selects a conversion by its source and target
/// types, never by its arguments, so each conversion is named on its own —
/// its kind's prefix (<c>Implicit</c>, none for an explicit one,
/// <c>Checked</c>), then <c>To</c> + the target type's spelling or
/// <c>From</c> + the source type's, with spec 013's CLR spellings (a
/// converted type's <c>[Name]</c> does not change them) and climb
/// (<c>ToInt64</c>, <c>ImplicitFromInt32</c>, <c>CheckedToInt32</c>). The core
/// computes it once per conversion and target from the Roslyn symbol, at the
/// declaration and at every use, the kind and direction words included; a
/// target adds only its own decoration (TypeScript's <c>__</c> and casing)
/// and claim.
/// </summary>
/// <param name="Direction">Out of or into the declaring type.</param>
/// <param name="Kind">Implicit, explicit or checked.</param>
/// <param name="Spelling">The converted type's spelling after the climb
/// (<c>Int32</c>, <c>NumericsDecimal</c>), with a positional marker
/// (<c>$1</c>) on a dead end; empty when <see cref="Pin"/> names the
/// conversion.</param>
/// <param name="Pin">The <c>[Name]</c> value for the emitting target, which
/// names the conversion whole (<c>toInt</c>); <c>null</c> when none.</param>
public sealed record IrConversionName(
    IrConversionDirection Direction,
    IrConversionKind Kind,
    string Spelling,
    string? Pin
)
{
    /// <summary>The candidate as one word: the pin as written, else the prefix and the spelling.</summary>
    public string Candidate => Pin ?? PrefixOf(Kind, Direction) + Spelling;

    /// <summary>The words a conversion's name starts with, before its spelling.</summary>
    public static string PrefixOf(IrConversionKind kind, IrConversionDirection direction) =>
        (kind is IrConversionKind.Explicit ? "" : kind.ToString()) + direction;
}
