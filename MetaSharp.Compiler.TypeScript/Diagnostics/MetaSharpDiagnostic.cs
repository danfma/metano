using Microsoft.CodeAnalysis;

namespace MetaSharp.Diagnostics;

/// <summary>
/// Severity of a transpiler diagnostic.
/// </summary>
public enum MetaSharpDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A transpiler diagnostic — surfaces issues like unsupported language features,
/// unresolved types, or ambiguous constructs at build time, with the original C#
/// source location preserved when available.
/// </summary>
public sealed record MetaSharpDiagnostic(
    MetaSharpDiagnosticSeverity Severity,
    string Code,
    string Message,
    Location? Location = null
)
{
    /// <summary>
    /// Formats the diagnostic in Roslyn-compatible style:
    /// path/to/file.cs(line,col): warning MS0001: message
    /// </summary>
    public string Format()
    {
        var severity = Severity switch
        {
            MetaSharpDiagnosticSeverity.Error => "error",
            MetaSharpDiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        if (Location is null)
            return $"{severity} {Code}: {Message}";

        var pos = Location.GetLineSpan();
        var path = pos.Path;
        var line = pos.StartLinePosition.Line + 1;
        var col = pos.StartLinePosition.Character + 1;
        return $"{path}({line},{col}): {severity} {Code}: {Message}";
    }
}

/// <summary>
/// Catalog of diagnostic codes used by the MetaSharp transpiler.
/// </summary>
public static class DiagnosticCodes
{
    /// <summary>MS0001 — A C# language feature is not supported by the transpiler.</summary>
    public const string UnsupportedFeature = "MS0001";

    /// <summary>MS0002 — A referenced type could not be resolved or is not transpilable.</summary>
    public const string UnresolvedType = "MS0002";

    /// <summary>MS0003 — An ambiguous construct that may produce incorrect output.</summary>
    public const string AmbiguousConstruct = "MS0003";

    /// <summary>MS0004 — Conflicting attributes on a single symbol.</summary>
    public const string ConflictingAttributes = "MS0004";
}
