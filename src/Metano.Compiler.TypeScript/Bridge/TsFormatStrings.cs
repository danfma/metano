using System.Text.RegularExpressions;
using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript target's <see cref="IFormatStringCapability"/>
/// (baseline FR-051): the .NET standard numeric format strings
/// <c>metano-runtime</c>'s formatters implement — <c>formatInteger</c> and
/// <c>formatInt64</c> (<c>integer-format.ts</c>, <c>int64-format.ts</c>) for
/// the integers, <c>formatDouble</c> (<c>double-format.ts</c>) for
/// <c>double</c>. The single place the supported-format rule is spelled on
/// the TypeScript side: <see cref="Int64Formatting"/> reads it too.
/// </summary>
internal sealed partial class TsFormatStrings : IFormatStringCapability
{
    /// <summary>The integer format .NET defines that the runtime does not implement (binary).</summary>
    private const char UnimplementedIntegerFormat = 'B';

    /// <summary>The custom date pattern that is a <c>DateOnly</c>'s ISO text.</summary>
    private const string IsoDatePattern = "yyyy-MM-dd";

    private const string GuidTypeName = "System.Guid";

    private const string DateOnlyTypeName = "System.DateOnly";

    private const string BigIntegerTypeName = "System.Numerics.BigInteger";

    private TsFormatStrings() { }

    public static TsFormatStrings Instance { get; } = new();

    /// <summary>
    /// The integers (not <c>char</c>) and <c>double</c>. <c>float</c> has
    /// no formatter: its value is not held at single precision, so its
    /// formatted digits would differ from .NET's.
    /// </summary>
    public bool HasFormatter(IrNumericKind kind) =>
        kind
            is IrNumericKind.SByte
                or IrNumericKind.Byte
                or IrNumericKind.Int16
                or IrNumericKind.UInt16
                or IrNumericKind.Int32
                or IrNumericKind.UInt32
                or IrNumericKind.Int64
                or IrNumericKind.UInt64
                or IrNumericKind.Double;

    /// <summary>
    /// A <c>double</c> format is formatted when it is standard (a letter
    /// .NET rejects throws the same <c>FormatException</c>); an integer
    /// format when <see cref="FormatsInteger"/> says so.
    /// <para>
    /// TODO(#662): a 64-bit integer accepts every format here because
    /// <see cref="Int64Formatting"/> reports its unsupported ones as MS0066,
    /// without a location; unifying the two diagnostics drops this exception.
    /// </para>
    /// </summary>
    public bool Formats(IrNumericKind kind, string format) =>
        kind switch
        {
            IrNumericKind.Int64 or IrNumericKind.UInt64 => true,
            IrNumericKind.Double => format.Length == 0 || IsStandard(format),
            _ => FormatsInteger(format),
        };

    /// <summary>
    /// The formats whose .NET text is, for every value, what a clause-free
    /// template-literal hole prints in TypeScript: a <c>Guid</c>'s <c>D</c>
    /// (the <c>UUID</c> string), a numeric enum's <c>D</c> (the enum holds the
    /// number), a <c>[StringEnum]</c>'s <c>G</c> / <c>F</c> when no member is
    /// renamed (it holds the member name), a <c>DateOnly</c>'s <c>O</c>
    /// (<c>Temporal.PlainDate</c> prints <c>yyyy-MM-dd</c>) and the one custom
    /// pattern listed, <c>DateOnly</c>'s <c>yyyy-MM-dd</c> (case-sensitive:
    /// <c>mm</c> is minutes), which is that same ISO text for every
    /// <c>DateOnly</c> (years 1 to 9999 print four digits), and a
    /// <c>BigInteger</c>'s <c>G</c> / <c>R</c> / <c>D</c> without a precision (a
    /// <c>bigint</c>'s digits).
    /// Both letter cases of a standard format mean the same there.
    /// </summary>
    public bool PrintsAsDefaultText(ITypeSymbol type, string format)
    {
        var letter = format.ToUpperInvariant();

        if (type.TypeKind is TypeKind.Enum)
            return EnumPrintsAsDefaultText(type, letter);

        return type.ToDisplayString() switch
        {
            GuidTypeName => letter is "D",
            DateOnlyTypeName => letter is "O" || format is IsoDatePattern,
            BigIntegerTypeName => letter is "G" or "R" or "D",
            _ => false,
        };
    }

    /// <summary>
    /// Whether the integer formatters implement <paramref name="format"/>:
    /// the empty format, or one letter and an optional precision other than
    /// <c>B</c>. A custom format (<c>0.00</c>, <c>#,##0</c>) is not.
    /// </summary>
    public static bool FormatsInteger(string format) =>
        format.Length == 0
        || IsStandard(format) && char.ToUpperInvariant(format[0]) != UnimplementedIntegerFormat;

    private static bool EnumPrintsAsDefaultText(ITypeSymbol type, string letter)
    {
        if (!type.HasStringEnum())
            return letter is "D";

        var holdsMemberNames = type.GetMembers()
            .OfType<IFieldSymbol>()
            .All(member => member.GetNameOverride(TargetLanguage.TypeScript) is null);

        return holdsMemberNames && letter is "G" or "F";
    }

    private static bool IsStandard(string format) => StandardFormat().IsMatch(format);

    [GeneratedRegex("^[A-Za-z][0-9]*$")]
    private static partial Regex StandardFormat();
}
