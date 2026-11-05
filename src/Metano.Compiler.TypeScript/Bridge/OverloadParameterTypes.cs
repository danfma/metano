namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Compares two overloads by the parameter types of their C# signature texts
/// (<c>R(int x)</c>, <c>R(int y)</c>: the same; <c>R(string x)</c>: not), the
/// parameter names left out. The claim pass uses it to tell a
/// re-implementation of an inherited fast path from a different overload that
/// shares its root name (spec 013 FR-034).
/// </summary>
internal static class OverloadParameterTypes
{
    public static bool Match(string? signature, string other) =>
        signature is not null && TypesOf(signature).SequenceEqual(TypesOf(other));

    /// <summary>
    /// The parameter types of <c>Name(T1 a, Dictionary&lt;K, V&gt; b)</c>: each
    /// top-level parameter without its trailing name.
    /// </summary>
    private static IEnumerable<string> TypesOf(string signature)
    {
        var open = signature.IndexOf('(');
        var parameters = signature[(open + 1)..^1];

        return TopLevelParts(parameters)
            .Select(parameter => parameter.Trim())
            .Where(parameter => parameter.Length > 0)
            .Select(parameter => parameter[..parameter.LastIndexOf(' ')]);
    }

    private static IEnumerable<string> TopLevelParts(string parameters)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            depth += parameters[i] switch
            {
                '<' or '(' or '[' => 1,
                '>' or ')' or ']' => -1,
                _ => 0,
            };

            if (parameters[i] == ',' && depth == 0)
            {
                yield return parameters[start..i];

                start = i + 1;
            }
        }

        yield return parameters[start..];
    }
}
