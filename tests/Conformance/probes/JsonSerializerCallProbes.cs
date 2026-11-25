using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conformance;

public sealed record JscBag(string Text, double Number, decimal Money, long Count);

[JsonSerializable(typeof(JscBag))]
public partial class JscContext : JsonSerializerContext;

/// <summary>
/// #741: the static <c>JsonSerializer.Serialize</c> / <c>Deserialize</c> calls over a
/// source-generated context's <c>JsonTypeInfo</c>, and <c>Context.Default</c>, map to
/// the runtime <c>JsonSerializer</c> and write / read the text STJ writes and reads.
/// </summary>
public static class JsonSerializerCallProbes
{
    public static string SerializeEscapesAndNumbers() =>
        JsonSerializer.Serialize(
            new JscBag("<a&b>'+`\"\\\né€/", 1e20, 12345678901234567890.123m, 9007199254740993L),
            JscContext.Default.JscBag
        );

    public static string SmallNumbersAndNegativeZero() =>
        JsonSerializer.Serialize(new JscBag("", 1e-7, 0.1m, -1L), JscContext.Default.JscBag)
        + "|"
        + JsonSerializer.Serialize(new JscBag("", -0.0, 0m, 0L), JscContext.Default.JscBag);

    public static string RoundTripKeepsExactDigits()
    {
        var json = JsonSerializer.Serialize(
            new JscBag("xé", 0.5, 79228162514264337593543950335m, long.MinValue),
            JscContext.Default.JscBag
        );
        var back = JsonSerializer.Deserialize(json, JscContext.Default.JscBag)!;

        return back.Text + "|" + back.Number + "|" + back.Money + "|" + back.Count;
    }

    public static string NullRoot() =>
        JsonSerializer.Serialize((JscBag?)null, JscContext.Default.JscBag)
        + "|"
        + (JsonSerializer.Deserialize("null", JscContext.Default.JscBag) is null);

    public static string InvalidJsonThrowsJsonException()
    {
        try
        {
            JsonSerializer.Deserialize("{bad", JscContext.Default.JscBag);

            return "no exception";
        }
        catch (JsonException)
        {
            return "JsonException";
        }
    }

    public static string NonFiniteNumberThrowsArgumentException()
    {
        try
        {
            return JsonSerializer.Serialize(
                new JscBag("", double.NaN, 0m, 0L),
                JscContext.Default.JscBag
            );
        }
        catch (ArgumentException)
        {
            return "ArgumentException";
        }
    }

    public static string DefaultIsOneInstance() =>
        ReferenceEquals(JscContext.Default, JscContext.Default)
        + "|"
        + (JscContext.Default is not null);
}
