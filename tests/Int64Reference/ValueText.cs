using System.Globalization;
using System.Numerics;

namespace Int64Reference;

/// <summary>
/// How a value is written to a fixture and named by its type, invariant and
/// exact: integers by their digits, a <c>char</c> by its code, a
/// <c>double</c> by its round-trip text, a <c>float</c> by the round-trip
/// text of its exact double value (so the TypeScript side compares the same
/// double), a <c>decimal</c> by its digits.
/// </summary>
internal static class ValueText
{
    public static string? Of(object? value) =>
        value switch
        {
            null => null,
            bool flag => flag ? "true" : "false",
            char character => ((int)character).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"No fixture text for '{value.GetType()}'."),
        };

    /// <summary>The type name the TypeScript side reads (<c>Int64</c>, <c>Int64?</c>, <c>BigInteger</c>, …).</summary>
    public static string NameOf(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? NameOf(underlying) + "?"
        : type == typeof(BigInteger) ? nameof(BigInteger)
        : type.Name;
}
