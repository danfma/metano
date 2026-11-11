using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The port through which a target says which .NET numeric format strings
/// its runtime formats with invariant-culture parity (baseline FR-051,
/// #323), so the extractor reports an interpolation clause or a
/// <c>ToString(format)</c> the target would otherwise drop or get wrong. The
/// core decides only C# semantics: which values are numeric, which formats
/// C# ignores, and which values are constants.
/// </summary>
public interface INumericFormatCapability
{
    /// <summary>
    /// Whether the target has a runtime formatter for values of
    /// <paramref name="kind"/>, which then decides a format known only at
    /// run time.
    /// </summary>
    bool HasFormatter(IrNumericKind kind);

    /// <summary>
    /// Whether the target's formatter formats a value of
    /// <paramref name="kind"/> with the constant <paramref name="format"/> as
    /// .NET does, a format both reject with the same
    /// <c>FormatException</c> included.
    /// </summary>
    bool Formats(IrNumericKind kind, string format);
}
