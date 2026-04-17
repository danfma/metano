using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Diagnostics;

/// <summary>
/// Severity of a transpiler diagnostic.
/// </summary>
public enum MetanoDiagnosticSeverity
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
public sealed record MetanoDiagnostic(
    MetanoDiagnosticSeverity Severity,
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
            MetanoDiagnosticSeverity.Error => "error",
            MetanoDiagnosticSeverity.Warning => "warning",
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
/// Catalog of diagnostic codes used by the Metano transpiler.
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

    /// <summary>MS0005 — A cyclic reference exists between generated TypeScript files.</summary>
    public const string CyclicReference = "MS0005";

    /// <summary>MS0006 — Invalid use of [ModuleEntryPoint] (multiple, non-void/Task return,
    /// or has parameters).</summary>
    public const string InvalidModuleEntryPoint = "MS0006";

    /// <summary>MS0007 — Cross-package resolution failure: the name in <c>package.json</c>
    /// diverges from the assembly's <c>[EmitPackage]</c>, OR a consumer references a type
    /// from an assembly that does not declare <c>[EmitPackage]</c> for the active target.</summary>
    public const string CrossPackageResolution = "MS0007";

    /// <summary>MS0008 — Conflicting <c>[EmitInFile]</c> grouping: types sharing the same
    /// file name belong to different namespaces, so the file would have an ambiguous
    /// folder placement.</summary>
    public const string EmitInFileConflict = "MS0008";

    /// <summary>MS0009 — Source frontend failed to load or compile the project (project
    /// file missing, MSBuild workspace failure, null compilation, or language-level
    /// errors reported by Roslyn).</summary>
    public const string FrontendLoadFailure = "MS0009";
}
