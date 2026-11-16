using System.Text.Encodings.Web;
using System.Text.Json;

namespace BclReference;

/// <summary>
/// Collects conformance rows and writes them as one JSON file, one row per line so a
/// regeneration diff reads row by row. A row names the runtime function under test, its
/// arguments, and what .NET produced: a value, or the exception type, parameter and message.
/// </summary>
internal sealed class FixtureWriter
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// An argument the call leaves out (an optional parameter's default): <c>undefined</c> in
    /// the TypeScript call, as opposed to the <c>null</c> C# passes.
    /// </summary>
    public static readonly object Omitted = new();

    private readonly List<string> _rows = [];

    /// <summary>Runs <paramref name="run"/> once per argument list and records each outcome.</summary>
    public void Each(
        string function,
        Func<object?[], object?> run,
        params object?[][] argumentLists
    )
    {
        foreach (var arguments in argumentLists)
            _rows.Add(Row(function, arguments, run));
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path) { NewLine = "\n" };

        writer.WriteLine("[");
        writer.WriteLine("  " + string.Join(",\n  ", _rows));
        writer.WriteLine("]");
    }

    private static string Row(string function, object?[] arguments, Func<object?[], object?> run)
    {
        var encodedArguments = arguments.Select(Encode).ToList();

        try
        {
            return Json(
                new Dictionary<string, object?>
                {
                    ["fn"] = function,
                    ["args"] = encodedArguments,
                    ["result"] = Encode(run(arguments)),
                }
            );
        }
        catch (Exception exception)
        {
            return Json(
                new Dictionary<string, object?>
                {
                    ["fn"] = function,
                    ["args"] = encodedArguments,
                    ["error"] = exception.GetType().Name,
                    ["param"] = (exception as ArgumentException)?.ParamName,
                    ["message"] = exception.Message,
                }
            );
        }
    }

    /// <summary>
    /// The JSON form of a value: text, a number, a boolean, an array, or <c>{"bigint": "…"}</c>
    /// for a 64-bit integer (a <c>bigint</c> in the TypeScript output).
    /// </summary>
    private static object? Encode(object? value) =>
        value switch
        {
            null => null,
            _ when ReferenceEquals(value, Omitted) => new Dictionary<string, bool>
            {
                ["undefined"] = true,
            },
            char c => c.ToString(),
            double d when !double.IsFinite(d) || (d == 0 && double.IsNegative(d)) => new Dictionary<
                string,
                string
            >
            {
                ["double"] = d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            long l => new Dictionary<string, string> { ["bigint"] = l.ToString() },
            ulong u => new Dictionary<string, string> { ["bigint"] = u.ToString() },
            char[] chars => chars.Select(c => c.ToString()).ToArray(),
            Array array => array.Cast<object?>().Select(Encode).ToArray(),
            Enum e => Convert.ToInt32(e),
            _ => value,
        };

    private static string Json(object value) => JsonSerializer.Serialize(value, LineOptions);
}
