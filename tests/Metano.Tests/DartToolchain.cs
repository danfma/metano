using System.Diagnostics;

namespace Metano.Tests;

internal static class DartToolchain
{
    public static bool IsAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;

        var executable = OperatingSystem.IsWindows() ? "dart.exe" : "dart";
        return path.Split(Path.PathSeparator)
            .Any(dir =>
                !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, executable))
            );
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(
        string[] args,
        string workingDirectory
    )
    {
        var psi = new ProcessStartInfo("dart")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dart {string.Join(' ', args)} timed out after 30s.");
        }

        var outputs = await Task.WhenAll(stdoutTask, stderrTask);
        return (process.ExitCode, outputs[0] + outputs[1]);
    }

    public static async Task AssertAnalyzesAsync(Dictionary<string, string> files)
    {
        if (!IsAvailable())
            Skip.Test("dart SDK not found on PATH.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"metano-dart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await WritePackageConfigAsync(tempDir);

            foreach (var (name, content) in files)
                await File.WriteAllTextAsync(Path.Combine(tempDir, name), content);

            var analyze = await RunAsync(["analyze"], tempDir);
            if (analyze.ExitCode != 0)
                throw new InvalidOperationException($"dart analyze failed:\n{analyze.Output}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    public static async Task<string> AnalyzeAndRunAsync(
        Dictionary<string, string> files,
        string mainDart
    )
    {
        if (!IsAvailable())
            Skip.Test("dart SDK not found on PATH.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"metano-dart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await WritePackageConfigAsync(tempDir);

            foreach (var (name, content) in files)
                await File.WriteAllTextAsync(Path.Combine(tempDir, name), content);

            await File.WriteAllTextAsync(Path.Combine(tempDir, "main.dart"), mainDart);

            var analyze = await RunAsync(["analyze"], tempDir);
            if (analyze.ExitCode != 0)
                throw new InvalidOperationException($"dart analyze failed:\n{analyze.Output}");

            var run = await RunAsync(["run", "main.dart"], tempDir);
            if (run.ExitCode != 0)
                throw new InvalidOperationException($"dart run failed:\n{run.Output}");

            return run.Output.Trim();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task WritePackageConfigAsync(string tempDir)
    {
        var runtimeDir = FindMetanoRuntimeDir();
        if (runtimeDir is null)
            return;

        var dartToolDir = Path.Combine(tempDir, ".dart_tool");
        Directory.CreateDirectory(dartToolDir);

        var rootUri = new Uri(runtimeDir.TrimEnd(Path.DirectorySeparatorChar) + "/").AbsoluteUri;
        var json = $$"""
            {
              "configVersion": 2,
              "packages": [
                {
                  "name": "metano_runtime",
                  "rootUri": "{{rootUri}}",
                  "packageUri": "lib/"
                }
              ]
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(dartToolDir, "package_config.json"), json);
    }

    private static string? FindMetanoRuntimeDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "targets", "dart", "metano_runtime");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }
}
