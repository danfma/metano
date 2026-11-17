using Metano.Annotations;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which numeric operations the core makes explicit for one target, decided
/// once from the target (<see cref="IrExtractionContext.NumericLowering"/>)
/// so no caller threads a flag: the conversions it lowers to an
/// <see cref="IrNumericConversion"/>, whether unchecked <c>int</c> /
/// <c>uint</c> arithmetic wraps, and which range facts may drop a
/// reduction. Dart keeps its <c>char</c> shapes, its unchecked 32-bit
/// arithmetic and its narrowings as they were (documented gaps, deferred
/// after v3), where the other targets read a <c>char</c> as its UTF-16 code
/// (spec 001 FR-071, FR-072), wrap that arithmetic (FR-074) and drop the
/// reductions the facts of FR-075 prove unneeded.
/// </summary>
/// <param name="LowersCharCodes">Whether a <c>char</c> is read and stored as its code.</param>
/// <param name="WrapsInt32Arithmetic">Whether unchecked <c>int</c> / <c>uint</c>
/// <c>+</c>, <c>-</c>, <c>*</c>, unary <c>-</c> and their stores are reduced
/// modulo 2^32 (an <see cref="IrIntegerOverflow"/> that wraps).</param>
/// <param name="ReadsCollectionAndLoopRanges">Whether the non-negative
/// <c>Length</c> / <c>Count</c> of an array, a <c>string</c> or a BCL
/// collection (<see cref="IntegerValueRanges"/>) and the bounded step of a
/// counting loop (<see cref="BoundedCountingLoop"/>) may drop a reduction
/// or a narrowing (FR-075).</param>
/// <param name="ComputesDecimalByMethods">Whether the target computes with a
/// <c>decimal</c> as a runtime value whose operators are methods, as
/// TypeScript's <c>Decimal</c> is and Dart's output is not: an integer of 32
/// bits or fewer read as a <c>decimal</c> is converted (FR-080), and the
/// compound assignments and <c>++</c> / <c>--</c> of a <c>decimal</c> store
/// the method call (<c>d = d.plus(x)</c>). The binary lowering
/// (<c>a + b</c> → <c>a.plus(b)</c>) and <c>BuildDecimalEquality</c> do not
/// read it yet and run for every target, Dart included (a latent Dart gap,
/// deferred).</param>
internal sealed record NumericLoweringRules(
    bool LowersCharCodes,
    bool WrapsInt32Arithmetic,
    bool ReadsCollectionAndLoopRanges,
    bool ComputesDecimalByMethods
)
{
    public static NumericLoweringRules For(TargetLanguage? target) =>
        new(
            LowersCharCodes: target != TargetLanguage.Dart,
            WrapsInt32Arithmetic: target != TargetLanguage.Dart,
            ReadsCollectionAndLoopRanges: target != TargetLanguage.Dart,
            ComputesDecimalByMethods: target != TargetLanguage.Dart
        );

    /// <summary>
    /// Whether an implicit conversion from <paramref name="from"/> to
    /// <paramref name="to"/> is made explicit: one that crosses a 64-bit
    /// integer (<see cref="NumericEndpoints.CrossesSixtyFourBits"/>), or a
    /// <c>char</c> read as a <c>double</c> / <c>float</c> or, when the target
    /// lowers char codes, as any number (FR-071), or an integer of 32 bits
    /// or fewer read as a <c>decimal</c> when the target lowers decimal
    /// values (FR-080).
    /// </summary>
    public bool NeedsImplicitNode(IrNumericEndpoint from, IrNumericEndpoint to) =>
        NumericEndpoints.CrossesSixtyFourBits(from, to)
        || IsLoweredCharRead(from, to)
        || IsLoweredIntegerToDecimal(from, to);

    /// <summary>
    /// Whether a conversion from <paramref name="from"/> to
    /// <paramref name="to"/> becomes an <see cref="IrNumericConversion"/>: a
    /// floating-point truncation (FR-070), a narrowing between two integers
    /// of 32 bits or fewer (FR-074), a <c>decimal</c> read as a
    /// <c>double</c> or <c>float</c>, a <c>char</c> read as a number or, when
    /// the target lowers char codes, made of an integer (FR-071, FR-072), a
    /// <c>decimal</c> made of such an integer when the target lowers decimal
    /// values (FR-080), and the conversions of
    /// <see cref="NumericEndpoints.CrossesSixtyFourBits"/>.
    /// </summary>
    public bool IsLoweredExplicitly(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsFloatingPointTruncation(from, to)
        || IsLoweredCharRead(from, to)
        || (LowersCharCodes && IrNumericConversion.IsCharStore(from, to))
        || IrNumericConversion.IsIntegerNarrowing(from, to)
        || IrNumericConversion.IsDecimalToBinaryFloatingPoint(from, to)
        || IsLoweredIntegerToDecimal(from, to)
        || NumericEndpoints.CrossesSixtyFourBits(from, to);

    private bool IsLoweredIntegerToDecimal(IrNumericEndpoint from, IrNumericEndpoint to) =>
        ComputesDecimalByMethods && IrNumericConversion.IsIntegerToDecimal(from, to);

    private bool IsLoweredCharRead(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsCharRead(from, to) && (LowersCharCodes || to.Kind.IsFloatingPoint());
}
