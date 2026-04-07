namespace MetaSharp.Compiler;

/// <summary>
/// Options shared by all transpiler targets. Target-specific flags
/// (e.g., TypeScript's --dist or --skip-package-json) live in the target CLI.
/// </summary>
public sealed record TranspileOptions(
    string ProjectPath,
    string OutputDir,
    bool ShowTimings = false,
    bool Clean = false
);
