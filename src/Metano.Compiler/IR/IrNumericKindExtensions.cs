namespace Metano.Compiler.IR;

/// <summary>
/// The facts of a C# numeric kind every lowering of spec 012 reads: sign,
/// width and family. One place, so the core, the TypeScript bridge and the
/// Dart printer cannot disagree.
/// </summary>
public static class IrNumericKindExtensions
{
    /// <summary>A two's-complement signed integer (<c>sbyte</c>, <c>short</c>, <c>int</c>, <c>long</c>).</summary>
    public static bool IsSigned(this IrNumericKind kind) =>
        kind
            is IrNumericKind.SByte
                or IrNumericKind.Int16
                or IrNumericKind.Int32
                or IrNumericKind.Int64;

    /// <summary>An integral kind of 64 bits or fewer (not <c>char</c>, not <c>BigInteger</c>).</summary>
    public static bool IsInteger(this IrNumericKind kind) =>
        kind
            is IrNumericKind.SByte
                or IrNumericKind.Byte
                or IrNumericKind.Int16
                or IrNumericKind.UInt16
                or IrNumericKind.Int32
                or IrNumericKind.UInt32
                or IrNumericKind.Int64
                or IrNumericKind.UInt64;

    /// <summary>An integral kind of 64 bits or fewer, or <c>char</c>.</summary>
    public static bool IsIntegerOrChar(this IrNumericKind kind) =>
        kind.IsInteger() || kind is IrNumericKind.Char;

    /// <summary><c>long</c> or <c>ulong</c> (<c>nint</c> / <c>nuint</c> included).</summary>
    public static bool IsSixtyFourBit(this IrNumericKind kind) =>
        kind is IrNumericKind.Int64 or IrNumericKind.UInt64;

    /// <summary><c>int</c> or <c>uint</c>: the kinds C# promotes narrower integer operands to.</summary>
    public static bool IsThirtyTwoBit(this IrNumericKind kind) =>
        kind is IrNumericKind.Int32 or IrNumericKind.UInt32;

    /// <summary><c>sbyte</c>, <c>byte</c>, <c>short</c> or <c>ushort</c>: an integer C# computes in <c>int</c>.</summary>
    public static bool IsNarrowerThanInt32(this IrNumericKind kind) =>
        kind
            is IrNumericKind.SByte
                or IrNumericKind.Byte
                or IrNumericKind.Int16
                or IrNumericKind.UInt16;

    /// <summary>An integer of 32 bits or fewer (not <c>char</c>).</summary>
    public static bool IsThirtyTwoBitOrNarrower(this IrNumericKind kind) =>
        kind.IsThirtyTwoBit() || kind.IsNarrowerThanInt32();

    /// <summary><c>float</c> or <c>double</c>.</summary>
    public static bool IsFloatingPoint(this IrNumericKind kind) =>
        kind is IrNumericKind.Single or IrNumericKind.Double;

    /// <summary>
    /// The bit width of an integer or <c>char</c> kind narrower than 64 bits
    /// (8, 16 or 32), the <c>n</c> of a narrowing conversion; 64 otherwise.
    /// </summary>
    public static int BitWidth(this IrNumericKind kind) =>
        kind switch
        {
            IrNumericKind.SByte or IrNumericKind.Byte => 8,
            IrNumericKind.Int16 or IrNumericKind.UInt16 or IrNumericKind.Char => 16,
            IrNumericKind.Int32 or IrNumericKind.UInt32 => 32,
            _ => 64,
        };

    /// <summary>
    /// Whether every value of the integer kind <paramref name="source"/> is a
    /// value of the integer kind <paramref name="target"/>, so converting
    /// between them cannot change the value: the same signedness and at
    /// least the width, or a signed target strictly wider than an unsigned
    /// source.
    /// </summary>
    public static bool HoldsEvery(this IrNumericKind target, IrNumericKind source)
    {
        if (!target.IsInteger() || !source.IsInteger())
            return false;

        if (target.IsSigned() == source.IsSigned())
            return target.BitWidth() >= source.BitWidth();

        return target.IsSigned() && target.BitWidth() > source.BitWidth();
    }

    /// <summary>
    /// An integer value of 32 bits or fewer (<c>sbyte</c>, <c>byte</c>,
    /// <c>short</c>, <c>ushort</c>, <c>int</c>, <c>uint</c>), not an enum:
    /// the kinds a double holds exactly.
    /// </summary>
    public static bool IsNarrowIntegerValue(this IrNumericEndpoint endpoint) =>
        !endpoint.IsEnum && endpoint.Kind.IsThirtyTwoBitOrNarrower();

    /// <summary>A 64-bit integer value, not an enum over one.</summary>
    public static bool IsSixtyFourBitValue(this IrNumericEndpoint endpoint) =>
        !endpoint.IsEnum && endpoint.Kind.IsSixtyFourBit();

    /// <summary>
    /// An integer value wider than a double holds exactly: a
    /// 64-bit integer or a <c>BigInteger</c>, not an enum.
    /// </summary>
    public static bool IsWideIntegerValue(this IrNumericEndpoint endpoint) =>
        !endpoint.IsEnum
        && (endpoint.Kind.IsSixtyFourBit() || endpoint.Kind is IrNumericKind.BigInteger);
}
