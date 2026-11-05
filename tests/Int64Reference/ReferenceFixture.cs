using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Int64Reference;

/// <summary>
/// Writes one fixture file: the signature of every public static method of
/// the twin modules and one case per combination of its parameters' boundary
/// operands, holding the result or the exception type and message .NET
/// produced (spec 012 FR-064, NFR-007). One signature and one case per line,
/// so a regeneration diff reads row by row.
/// </summary>
internal static class ReferenceFixture
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string path, params Type[] modules)
    {
        var methods = modules
            .SelectMany(module =>
                module
                    .GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                    )
                    .OrderBy(method => method.MetadataToken)
            )
            .ToList();

        var signatures = methods.Select(method =>
            $"{Json(method.Name)}: {Json(SignatureOf(method))}"
        );

        var cases = methods.SelectMany(method =>
            ArgumentsOf(method).Select(arguments => Json(CaseOf(method, arguments)))
        );

        using var writer = new StreamWriter(path) { NewLine = "\n" };

        writer.WriteLine("{");
        writer.WriteLine("  \"signatures\": {");
        WriteLines(writer, signatures);
        writer.WriteLine("  },");
        writer.WriteLine("  \"cases\": [");
        WriteLines(writer, cases);
        writer.WriteLine("  ]");
        writer.WriteLine("}");
    }

    private static void WriteLines(StreamWriter writer, IEnumerable<string> lines)
    {
        var entries = lines.ToList();

        if (entries.Count > 0)
            writer.WriteLine("    " + string.Join(",\n    ", entries));
    }

    private static string Json(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), LineOptions);

    private static FixtureSignature SignatureOf(MethodInfo method) =>
        new(
            [
                .. method
                    .GetParameters()
                    .Select(parameter => ValueText.NameOf(parameter.ParameterType)),
            ],
            ValueText.NameOf(method.ReturnType)
        );

    private static FixtureCase CaseOf(MethodInfo method, object?[] arguments)
    {
        var argumentTexts = arguments.Select(ValueText.Of).ToArray();

        try
        {
            return new FixtureResult(
                method.Name,
                argumentTexts,
                ValueText.Of(method.Invoke(null, arguments))
            );
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is { } thrown)
        {
            return new FixtureThrow(
                method.Name,
                argumentTexts,
                thrown.GetType().Name,
                thrown.Message
            );
        }
    }

    /// <summary>Every combination of the boundary operands of the method's parameters, in order.</summary>
    private static IEnumerable<object?[]> ArgumentsOf(MethodInfo method) =>
        method
            .GetParameters()
            .Select(BoundaryOperands.For)
            .Aggregate(
                (IEnumerable<object?[]>)
                    [
                        [],
                    ],
                (combinations, operands) =>
                    combinations.SelectMany(prefix =>
                        operands.Select(operand => (object?[])[.. prefix, operand])
                    )
            );
}

/// <summary>A twin method's parameter and return type names.</summary>
internal sealed record FixtureSignature(string[] Parameters, string Returns);

/// <summary>One call of a twin method with its arguments' fixture text.</summary>
internal abstract record FixtureCase(
    [property: JsonPropertyOrder(-1)] string Case,
    [property: JsonPropertyOrder(-1)] string?[] Args
);

/// <summary>A case whose call returned; <see cref="Result"/> is <c>null</c> for a null result.</summary>
internal sealed record FixtureResult(string Case, string?[] Args, string? Result)
    : FixtureCase(Case, Args);

/// <summary>A case whose call threw.</summary>
internal sealed record FixtureThrow(string Case, string?[] Args, string Throws, string Message)
    : FixtureCase(Case, Args);
