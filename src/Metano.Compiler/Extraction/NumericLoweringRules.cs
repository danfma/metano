using System.Diagnostics.CodeAnalysis;
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
/// <param name="RoundsSinglePrecision">Whether a <c>float</c> value is kept
/// in single precision (#695), as the target holds both binary types in one
/// 64-bit type (JavaScript's <c>number</c>): a <c>double</c>, <c>int</c> or
/// <c>uint</c> converted to <c>float</c> is rounded
/// (<see cref="IrNumericConversion.IsRoundedToSingle"/>), and so is every
/// inexact <c>float</c> arithmetic result and store
/// (<see cref="SinglePrecisionRounding"/>). Dart keeps its output as it
/// was.</param>
/// <param name="UsesEnumUnderlyingIntegers">Whether numeric enum conversions,
/// arithmetic and folded constants use their underlying integer endpoints
/// (spec 011 FR-044, #701 and #708). Dart keeps its enum output as it was
/// (deferred after v3).</param>
internal sealed record NumericLoweringRules(
    bool LowersCharCodes,
    bool WrapsInt32Arithmetic,
    bool ReadsCollectionAndLoopRanges,
    bool ComputesDecimalByMethods,
    bool RoundsSinglePrecision,
    bool UsesEnumUnderlyingIntegers
)
{
    public static NumericLoweringRules For(TargetLanguage? target) =>
        new(
            LowersCharCodes: target != TargetLanguage.Dart,
            WrapsInt32Arithmetic: target != TargetLanguage.Dart,
            ReadsCollectionAndLoopRanges: target != TargetLanguage.Dart,
            ComputesDecimalByMethods: target != TargetLanguage.Dart,
            RoundsSinglePrecision: target != TargetLanguage.Dart,
            UsesEnumUnderlyingIntegers: target != TargetLanguage.Dart
        );

    /// <summary>
    /// The endpoint the target computes <paramref name="endpoint"/> as: a
    /// numeric enum's underlying integer when the target uses integer
    /// endpoints for enums, otherwise the endpoint itself. Conversions,
    /// arithmetic and constants share this representation.
    /// </summary>
    [return: NotNullIfNotNull(nameof(endpoint))]
    public IrNumericEndpoint? RepresentationOf(IrNumericEndpoint? endpoint) =>
        UsesEnumUnderlyingIntegers ? endpoint?.AsUnderlyingInteger() : endpoint;

    /// <summary>
    /// Whether an implicit conversion from <paramref name="from"/> to
    /// <paramref name="to"/> is made explicit: one that crosses a 64-bit
    /// integer (<see cref="NumericEndpoints.CrossesSixtyFourBits"/>), or a
    /// <c>char</c> read as a <c>double</c> / <c>float</c> or, when the target
    /// lowers char codes, as any number (FR-071), or an integer of 32 bits
    /// or fewer read as a <c>decimal</c> when the target lowers decimal
    /// values (FR-080), or an <c>int</c> / <c>uint</c> read as a
    /// <c>float</c> when the target rounds single precision (#695).
    /// </summary>
    public bool NeedsImplicitNode(IrNumericEndpoint from, IrNumericEndpoint to) =>
        CrossesSixtyFourBits(from, to)
        || IsLoweredCharRead(from, to)
        || IsLoweredToDecimal(from, to)
        || IsLoweredToSingle(from, to);

    /// <summary>
    /// Whether a conversion from <paramref name="from"/> to
    /// <paramref name="to"/> becomes an <see cref="IrNumericConversion"/>: a
    /// floating-point truncation (FR-070), a narrowing between two integers
    /// of 32 bits or fewer (FR-074), a <c>decimal</c> read as a
    /// <c>double</c> or <c>float</c> or as an integer of 32 bits or fewer
    /// (every target, #604: the target renders it), a <c>char</c> read as a number or, when
    /// the target lowers char codes, made of an integer or a <c>decimal</c> (FR-071, FR-072, #695), a
    /// <c>decimal</c> made of such an integer or of a <c>double</c> /
    /// <c>float</c> when the target lowers decimal values (FR-080, #695), and
    /// a <c>double</c>, <c>int</c> or <c>uint</c> rounded to a <c>float</c> when the target rounds
    /// single precision (#695), and the conversions of
    /// <see cref="NumericEndpoints.CrossesSixtyFourBits"/>.
    /// </summary>
    public bool IsLoweredExplicitly(IrNumericEndpoint from, IrNumericEndpoint to)
    {
        var source = RepresentationOf(from);
        var target = RepresentationOf(to);

        return IrNumericConversion.IsFloatingPointTruncation(source, target)
            || IsLoweredCharRead(source, target)
            || (LowersCharCodes && IrNumericConversion.IsCharStore(source, target))
            || (LowersCharCodes && IrNumericConversion.IsDecimalToChar(source, target))
            || IrNumericConversion.IsIntegerNarrowing(source, target)
            || IrNumericConversion.IsDecimalToBinaryFloatingPoint(source, target)
            || IrNumericConversion.IsDecimalToNarrowInteger(source, target)
            || IsLoweredToDecimal(source, target)
            || IsLoweredToSingle(source, target)
            || CrossesSixtyFourBits(source, target);
    }

    /// <summary>
    /// <see cref="NumericEndpoints.CrossesSixtyFourBits"/> between the
    /// endpoints as the target computes them (<see cref="RepresentationOf"/>):
    /// a numeric enum represented as its underlying integer is that integer.
    /// </summary>
    private bool CrossesSixtyFourBits(IrNumericEndpoint from, IrNumericEndpoint to) =>
        NumericEndpoints.CrossesSixtyFourBits(RepresentationOf(from), RepresentationOf(to));

    private bool IsLoweredToDecimal(IrNumericEndpoint from, IrNumericEndpoint to) =>
        ComputesDecimalByMethods
        && (
            IrNumericConversion.IsIntegerToDecimal(from, to)
            || IrNumericConversion.IsBinaryFloatingPointToDecimal(from, to)
        );

    private bool IsLoweredToSingle(IrNumericEndpoint from, IrNumericEndpoint to) =>
        RoundsSinglePrecision && IrNumericConversion.IsRoundedToSingle(from, to);

    private bool IsLoweredCharRead(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsCharRead(from, to) && (LowersCharCodes || to.Kind.IsFloatingPoint());
}
