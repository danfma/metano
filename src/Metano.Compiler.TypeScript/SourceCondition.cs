using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// The name of the package.json export condition under which the generated package
/// exposes its <c>.ts</c> sources (<c>exports</c> and <c>imports</c> entries). The
/// default, <c>source</c>, is generic, so a consumer that enables it globally also
/// opts every third-party package that exports <c>source</c> into raw-source
/// resolution; a package-specific name (e.g. <c>metano-source</c>) scopes it to the
/// generated package. #424.
/// <para>
/// <see cref="Default"/> equals <c>default(SourceCondition)</c>; every other value is
/// created through <see cref="TryFrom"/>, which validates once.
/// </para>
/// </summary>
public readonly record struct SourceCondition
{
    public const string DefaultName = "source";

    /// <summary>
    /// Conditions Node itself defines. Reusing one as the source condition would hijack
    /// ordinary resolution, so they are rejected.
    /// </summary>
    private static readonly HashSet<string> BuiltInConditions =
    [
        "import",
        "require",
        "default",
        "types",
        "node",
        "node-addons",
        "browser",
        "module-sync",
    ];

    /// <summary>
    /// Conditions bundlers enable by default (Vite, webpack, esbuild). Choosing one would
    /// route this package to its <c>.ts</c> sources in ordinary (e.g. production) builds.
    /// </summary>
    private static readonly HashSet<string> BundlerDefaultConditions =
    [
        "module",
        "development",
        "production",
    ];

    private readonly string? _name;

    private SourceCondition(string name) => _name = name;

    /// <summary>The generic <c>source</c> condition; equals <c>default</c>.</summary>
    public static SourceCondition Default => default;

    public string Name => _name ?? DefaultName;

    public bool IsDefault => Name == DefaultName;

    /// <summary>
    /// Parses a raw <c>--source-condition</c> value. Blank means unset and yields
    /// <see cref="Default"/>. A name is valid when it is made of letters, digits, <c>-</c>,
    /// <c>_</c>, <c>.</c> and <c>:</c>, is not all digits (Node rejects numeric export keys),
    /// and is neither a Node built-in nor a bundler-default condition. An invalid value
    /// yields <see cref="Default"/>, fails, and carries the error diagnostic.
    /// </summary>
    public static bool TryFrom(
        string? raw,
        out SourceCondition condition,
        out MetanoDiagnostic? diagnostic
    )
    {
        condition = Default;
        diagnostic = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var name = raw.Trim();
        var problem = ProblemWith(name);

        if (problem is not null)
        {
            diagnostic = new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidSourceCondition,
                $"Invalid source condition '{name}': {problem}. Use a name such as "
                    + "'metano-source' (MSBuild: MetanoSourceCondition, CLI: --source-condition)."
            );

            return false;
        }

        condition = new SourceCondition(name);

        return true;
    }

    public override string ToString() => Name;

    private static string? ProblemWith(string name)
    {
        if (!name.All(IsConditionCharacter))
            return "only letters, digits, '-', '_', '.' and ':' are allowed";

        if (name.All(char.IsAsciiDigit))
            return "a name made only of digits is rejected by Node (invalid exports key)";

        if (BuiltInConditions.Contains(name))
            return "it is a built-in Node resolution condition";

        if (BundlerDefaultConditions.Contains(name))
            return "it is a condition bundlers enable by default, so it would route the package to its sources in ordinary builds";

        return null;
    }

    private static bool IsConditionCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
}
