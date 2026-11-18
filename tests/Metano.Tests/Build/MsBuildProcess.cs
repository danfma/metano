using System.Diagnostics;

namespace Metano.Tests.Build;

/// <summary>The exit code and the combined standard output and error of one <c>dotnet msbuild</c> run.</summary>
public sealed record MsBuildRun(int ExitCode, string Output);

/// <summary>
/// Runs <c>dotnet msbuild</c> against a scratch project, the way the build tests evaluate the
/// shipped Metano MSBuild targets (#433, #328).
/// </summary>
public static class MsBuildProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs <c>dotnet msbuild</c> with <paramref name="arguments"/> in
    /// <paramref name="workingDirectory"/>, without node reuse and with the global tool store
    /// redirected to <paramref name="cliHome"/>.
    /// </summary>
    public static async Task<MsBuildRun> Run(
        string workingDirectory,
        string cliHome,
        IEnumerable<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        string[] fixedArguments =
        [
            "msbuild",
            "-nologo",
            "-nodeReuse:false",
            "-verbosity:minimal",
            "-p:IntermediateOutputPath=obj/",
            $"-p:DOTNET_CLI_HOME={cliHome}",
        ];

        foreach (var argument in fixedArguments.Concat(arguments))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet could not be started.");

        using var timeout = new CancellationTokenSource(Timeout);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);

        await process.WaitForExitAsync(timeout.Token);

        return new MsBuildRun(process.ExitCode, await output + await errors);
    }
}
