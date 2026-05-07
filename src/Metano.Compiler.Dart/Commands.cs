using ConsoleAppFramework;
using Metano.Compiler;

namespace Metano.Compiler.Dart;

public class Commands
{
    /// <summary>
    /// Transpile annotated C# types to Dart.
    /// </summary>
    /// <param name="project">-p, Path to the C# project file (.csproj).</param>
    /// <param name="output">-o, Output directory for generated Dart files.</param>
    /// <param name="time">-t, Show compilation and transpilation timings.</param>
    /// <param name="clean">-c, Clean output directory before generating.</param>
    /// <param name="filePrefix">Opaque text written verbatim at the top of every generated file, followed by a single newline. Use for `// @generated` provenance markers or Dart linter directives.</param>
    /// <param name="dryRun">Run the full pipeline but do NOT write any files. Print a preflight summary (file count + total line count + per-file paths) instead.</param>
    [Command("")]
    public async Task Transpile(
        string project,
        string output,
        bool time = false,
        bool clean = false,
        string? filePrefix = null,
        bool dryRun = false
    )
    {
        var target = new DartTarget();

        var options = new TranspileOptions(
            ProjectPath: project,
            OutputDir: output,
            ShowTimings: time,
            Clean: clean,
            FilePrefix: filePrefix,
            DryRun: dryRun
        );

        var result = await TranspilerHost.RunAsync(options, target);
        if (!result.Success)
            Environment.Exit(1);
    }
}
