using System.Numerics;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// <c>MS0064</c> (spec 012 FR-058): a TypeScript enum member is a
/// <c>number</c>, so a member of an enum over a 64-bit integer loses its exact
/// value beyond 2^53, and a <c>[Flags]</c> member of 2^31 or more is truncated
/// by the 32-bit JS bit operators flags code runs on. A TypeScript concern,
/// reported where the enum is emitted.
/// </summary>
internal static class Int64EnumPrecision
{
    private static readonly BigInteger TwoToThe31 = BigInteger.One << 31;

    /// <summary>The value a TS enum member over <paramref name="value"/> holds: the nearest double.</summary>
    public static BigInteger NearestDouble(BigInteger value) => new((double)value);

    /// <summary>Reports every member of <paramref name="enumType"/> whose value is at risk.</summary>
    public static void Check(INamedTypeSymbol enumType, Action<MetanoDiagnostic> report)
    {
        if (!IsSixtyFourBit(enumType.EnumUnderlyingType))
            return;

        var isFlags = IsFlags(enumType);

        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.ConstantValue is not { } constant)
                continue;

            if (
                PrecisionWarningMessage(enumType, member, ToBigInteger(constant), isFlags) is
                { } message
            )
                report(Warning(member, message));
        }
    }

    private static string? PrecisionWarningMessage(
        INamedTypeSymbol enumType,
        IFieldSymbol member,
        BigInteger value,
        bool isFlags
    )
    {
        var name = $"{enumType.Name}.{member.Name}";
        var nearest = NearestDouble(value);

        if (nearest != value)
        {
            return $"Enum member '{name}' has the value {value}, which TypeScript enum members "
                + $"cannot hold exactly; it is emitted as {nearest} (spec 012 FR-058).";
        }

        if (isFlags && IsBeyondThirtyTwoBits(value))
        {
            return $"Flags enum member '{name}' has the value {value}, beyond the 32 bits "
                + "JavaScript bit operators keep on a number; '|', '&' and HasFlag lose it in "
                + "TypeScript (spec 012 FR-058).";
        }

        return null;
    }

    private static bool IsBeyondThirtyTwoBits(BigInteger value) =>
        value >= TwoToThe31 || value < -TwoToThe31;

    private static MetanoDiagnostic Warning(IFieldSymbol member, string message) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.Int64EnumValuePrecision,
            message,
            member.Locations.FirstOrDefault()
        );

    // Reads the Roslyn symbol, not an IR endpoint: the enum is emitted from
    // its declaration, so the core's endpoint classification is not at hand.
    private static bool IsSixtyFourBit(ITypeSymbol? type) =>
        type?.SpecialType
            is SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr;

    private static bool IsFlags(INamedTypeSymbol enumType) =>
        enumType
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"
            );

    /// <summary>The exact value of a 64-bit enum constant (a <c>long</c> or a <c>ulong</c>).</summary>
    internal static BigInteger ToBigInteger(object constant) =>
        constant is ulong unsigned
            ? new BigInteger(unsigned)
            : new BigInteger(Convert.ToInt64(constant));
}
