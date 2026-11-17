using Metano.Annotations;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which numeric operations the core makes explicit for one target, decided
/// once from the target (<see cref="IrExtractionContext.NumericLowering"/>)
/// so no caller threads a flag: the conversions it lowers to an
/// <see cref="IrNumericConversion"/>, and whether unchecked <c>int</c> /
/// <c>uint</c> arithmetic wraps. Dart keeps its <c>char</c> shapes and its
/// unchecked 32-bit arithmetic as they were (both documented gaps), where
/// the other targets read a <c>char</c> as its UTF-16 code (spec 001 FR-071,
/// FR-072) and wrap that arithmetic (FR-074).
/// </summary>
/// <param name="LowersCharCodes">Whether a <c>char</c> is read and stored as its code.</param>
/// <param name="WrapsInt32Arithmetic">Whether unchecked <c>int</c> / <c>uint</c>
/// <c>+</c>, <c>-</c>, <c>*</c>, unary <c>-</c> and their stores are reduced
/// modulo 2^32 (an <see cref="IrIntegerOverflow"/> that wraps).</param>
internal sealed record NumericLoweringRules(bool LowersCharCodes, bool WrapsInt32Arithmetic)
{
    public static NumericLoweringRules For(TargetLanguage? target) =>
        new(
            LowersCharCodes: target != TargetLanguage.Dart,
            WrapsInt32Arithmetic: target != TargetLanguage.Dart
        );

    /// <summary>
    /// Whether an implicit conversion from <paramref name="from"/> to
    /// <paramref name="to"/> is made explicit: one that crosses a 64-bit
    /// integer (<see cref="NumericEndpoints.CrossesSixtyFourBits"/>), or a
    /// <c>char</c> read as a <c>double</c> / <c>float</c> or, when the target
    /// lowers char codes, as any number (FR-071).
    /// </summary>
    public bool NeedsImplicitNode(IrNumericEndpoint from, IrNumericEndpoint to) =>
        NumericEndpoints.CrossesSixtyFourBits(from, to) || IsLoweredCharRead(from, to);

    /// <summary>
    /// Whether a conversion from <paramref name="from"/> to
    /// <paramref name="to"/> becomes an <see cref="IrNumericConversion"/>: a
    /// floating-point truncation (FR-070), a narrowing between two integers
    /// of 32 bits or fewer (FR-074), a <c>decimal</c> read as a
    /// <c>double</c> or <c>float</c>, a <c>char</c> read as a number or, when
    /// the target lowers char codes, made of an integer (FR-071, FR-072), and
    /// the conversions of <see cref="NumericEndpoints.CrossesSixtyFourBits"/>.
    /// </summary>
    public bool IsLoweredExplicitly(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsFloatingPointTruncation(from, to)
        || IsLoweredCharRead(from, to)
        || (LowersCharCodes && IrNumericConversion.IsCharStore(from, to))
        || IrNumericConversion.IsIntegerNarrowing(from, to)
        || IrNumericConversion.IsDecimalToBinaryFloatingPoint(from, to)
        || NumericEndpoints.CrossesSixtyFourBits(from, to);

    private bool IsLoweredCharRead(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsCharRead(from, to) && (LowersCharCodes || to.Kind.IsFloatingPoint());
}
