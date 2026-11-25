namespace Metano.Compiler.Diagnostics;

/// <summary>
/// How a run treats Metano warnings, mirroring MSBuild's <c>TreatWarningsAsErrors</c>,
/// <c>WarningsAsErrors</c>, <c>WarningsNotAsErrors</c> and <c>NoWarn</c> for the
/// <c>MS</c>-prefixed codes. The build targets pass the project's settings to the CLI, so a
/// Metano warning fails the build exactly when the project says warnings fail it.
/// </summary>
/// <param name="TreatWarningsAsErrors">Every warning not listed in
/// <paramref name="WarningsNotAsErrors"/> becomes an error.</param>
/// <param name="WarningsAsErrors">Codes that become errors even without
/// <paramref name="TreatWarningsAsErrors"/>.</param>
/// <param name="WarningsNotAsErrors">Codes that stay warnings, which wins over the two
/// escalating settings.</param>
/// <param name="NoWarn">Codes whose warnings are dropped, which wins over escalation.</param>
public sealed record DiagnosticPolicy(
    bool TreatWarningsAsErrors = false,
    IReadOnlySet<string>? WarningsAsErrors = null,
    IReadOnlySet<string>? WarningsNotAsErrors = null,
    IReadOnlySet<string>? NoWarn = null
)
{
    public static DiagnosticPolicy Default { get; } = new();

    /// <summary>
    /// Builds a policy from the CLI values. Each list is separated by commas, semicolons
    /// or white space; entries that are not a Metano code (<c>MS</c> and four digits) are
    /// ignored, because the same MSBuild properties also carry compiler codes.
    /// </summary>
    public static DiagnosticPolicy FromCommandLine(
        bool warnAsError,
        string? warningsAsErrors,
        string? warningsNotAsErrors,
        string? noWarn
    ) =>
        new(
            warnAsError,
            ParseCodes(warningsAsErrors),
            ParseCodes(warningsNotAsErrors),
            ParseCodes(noWarn)
        );

    /// <summary>
    /// Applies the policy: suppressed warnings are dropped, escalated ones become errors
    /// (the message and location are kept), everything else passes unchanged.
    /// </summary>
    public IReadOnlyList<MetanoDiagnostic> Apply(IReadOnlyList<MetanoDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0 || IsInert)
            return diagnostics;

        var result = new List<MetanoDiagnostic>(diagnostics.Count);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != MetanoDiagnosticSeverity.Warning)
            {
                result.Add(diagnostic);

                continue;
            }

            if (Contains(NoWarn, diagnostic.Code))
                continue;

            result.Add(
                IsEscalated(diagnostic.Code)
                    ? diagnostic with
                    {
                        Severity = MetanoDiagnosticSeverity.Error,
                    }
                    : diagnostic
            );
        }

        return result;
    }

    private bool IsInert =>
        !TreatWarningsAsErrors
        && WarningsAsErrors is not { Count: > 0 }
        && NoWarn is not { Count: > 0 };

    private bool IsEscalated(string code)
    {
        if (Contains(WarningsNotAsErrors, code))
            return false;

        return TreatWarningsAsErrors || Contains(WarningsAsErrors, code);
    }

    private static bool Contains(IReadOnlySet<string>? codes, string code) =>
        codes is not null && codes.Contains(code);

    private static HashSet<string> ParseCodes(string? value)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(value))
            return codes;

        var separators = new[] { ',', ';', ' ', '\t', '\r', '\n' };

        foreach (var entry in value.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsMetanoCode(entry))
                codes.Add(entry);
        }

        return codes;
    }

    private static bool IsMetanoCode(string entry) =>
        entry.Length == 6
        && entry.StartsWith("MS", StringComparison.OrdinalIgnoreCase)
        && entry.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
}
