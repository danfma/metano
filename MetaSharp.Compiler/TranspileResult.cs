namespace MetaSharp.Compiler;

/// <summary>
/// Outcome of a TranspilerHost run. When <see cref="Success"/> is true the files have
/// already been written to the output directory; the caller may still perform target-specific
/// post-processing such as writing a package.json.
/// </summary>
public sealed record TranspileResult(
    bool Success,
    IReadOnlyList<GeneratedFile> Files,
    int WarningCount,
    int ErrorCount
);
