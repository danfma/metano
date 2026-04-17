using ConsoleAppFramework;
using Metano.Compiler;

namespace Metano.Dart;

public class Commands
{
    /// <summary>
    /// Transpile annotated C# types to Dart.
    /// </summary>
    /// <param name="project">-p, Path to the C# project file (.csproj).</param>
    /// <param name="output">-o, Output directory for generated Dart files.</param>
    /// <param name="time">-t, Show compilation and transpilation timings.</param>
    /// <param name="clean">-c, Clean output directory before generating.</param>
    [Command("")]
    public async Task Transpile(
        string project,
        string output,
        bool time = false,
        bool clean = false
    )
    {
        var target = new DartTarget();

        var options = new TranspileOptions(
            ProjectPath: project,
            OutputDir: output,
            ShowTimings: time,
            Clean: clean
        );

        var result = await TranspilerHost.RunAsync(options, target);
        if (!result.Success)
            Environment.Exit(1);
    }
}
