namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How the TypeScript target moves a generated name out of the way of one
/// already taken: it appends <see cref="Mark"/> until the name is free
/// (<c>fromCents</c> → <c>fromCents$</c>, spec 013 FR-004).
/// </summary>
internal static class TsFreeName
{
    /// <summary>The mark a claimed name takes: <c>$</c>.</summary>
    public const string Mark = "$";

    /// <summary>The first spelling of <paramref name="candidate"/> that is not taken.</summary>
    public static string FirstFree(string candidate, Func<string, bool> isTaken)
    {
        var name = candidate;

        while (isTaken(name))
            name += Mark;

        return name;
    }
}
