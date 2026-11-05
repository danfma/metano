using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The <c>decimal</c> rows of the reference matrix (spec 012 FR-016). A static
/// class rather than <c>[NoContainer]</c> functions, whose signatures map
/// <c>decimal</c> to <c>number</c> today; emitted beside
/// <see cref="Conversions"/>.
/// </summary>
[EmitInFile("conversions")]
public static class DecimalConversions
{
    public static decimal ToDecimal(long value) => value;

    public static decimal UnsignedToDecimal(ulong value) => value;

    public static long FromDecimal(decimal value) => (long)value;

    public static ulong UnsignedFromDecimal(decimal value) => (ulong)value;
}
