using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Metano.Tests.Build;

/// <summary>
/// Issue #433: the <c>Metano.Build</c> target reruns after a Metano upgrade because its
/// <c>Inputs</c> list a toolchain key file that <c>_MetanoWriteToolchainKey</c> rewrites
/// only when the toolchain changes. Each test evaluates the shipped target with a real
/// <c>dotnet msbuild</c> run against a bare project, with the global tool store redirected
/// into a temporary <c>DOTNET_CLI_HOME</c>.
/// </summary>
public class ToolchainKeyTests
{
    private const string ProjectFileName = "App.proj";

    private const string ToolStoreRelativePath = ".dotnet/tools/.store/metano.compiler.typescript";

    private static readonly TimeSpan MsBuildTimeout = TimeSpan.FromMinutes(2);

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-toolchain-key-")
        .FullName;

    private string ProjectDir => Path.Combine(_root, "app");

    private string CliHome => Path.Combine(_root, "home");

    private string KeyPath =>
        Path.Combine(ProjectDir, "obj", $"{ProjectFileName}.MetanoToolchain.cache");

    [Before(Test)]
    public void CreateProject()
    {
        Directory.CreateDirectory(ProjectDir);

        var targetsPath = Path.Combine(
            AppContext.BaseDirectory,
            "MetanoBuild",
            "Metano.Build.targets"
        );

        File.WriteAllText(
            Path.Combine(ProjectDir, ProjectFileName),
            $"""<Project><Import Project="{targetsPath}" /></Project>"""
        );
    }

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task Key_ListsEveryGlobalToolVersionAsExpandedText()
    {
        InstallGlobalTool("2.9.0");
        InstallGlobalTool("2.10.0");

        var key = await WriteKey();

        await Assert.That(key).DoesNotContain("System.String[]");
        await Assert.That(key).DoesNotContain("$(");
        await Assert.That(key).Contains("globalTool=2.9.0");
        await Assert.That(key).Contains("globalTool=2.10.0");
    }

    [Test]
    public async Task GlobalToolUpdate_ChangesTheKey()
    {
        InstallGlobalTool("2.9.0");
        var before = await WriteKey();

        UninstallGlobalTool("2.9.0");
        InstallGlobalTool("2.10.0");
        var after = await WriteKey();

        await Assert.That(after).IsNotEqualTo(before);
    }

    [Test]
    public async Task ToolManifestUpdate_ChangesTheKey()
    {
        WriteToolManifest("2.9.0");
        var before = await WriteKey();

        WriteToolManifest("2.10.0");
        var after = await WriteKey();

        await Assert.That(before).Contains("toolManifest=");
        await Assert.That(after).IsNotEqualTo(before);
    }

    /// <summary>
    /// An unchanged toolchain must leave the key's timestamp alone, or every build would
    /// rerun the transpile target.
    /// </summary>
    [Test]
    public async Task UnchangedToolchain_KeepsTheKeyTimestamp()
    {
        InstallGlobalTool("2.10.0");
        WriteToolManifest("2.10.0");
        await WriteKey();
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var past = oneHourAgo.AddTicks(-(oneHourAgo.Ticks % TimeSpan.TicksPerSecond));
        File.SetLastWriteTimeUtc(KeyPath, past);

        await WriteKey();

        await Assert.That(File.GetLastWriteTimeUtc(KeyPath)).IsEqualTo(past);
    }

    [Test]
    public async Task DevMode_NamesTheCompilerProjectInsteadOfTheTool()
    {
        InstallGlobalTool("2.10.0");
        WriteToolManifest("2.10.0");

        var key = await WriteKey("-p:MetanoCompilerProject=../compiler/Compiler.csproj");

        await Assert.That(key).Contains("compilerProject=../compiler/Compiler.csproj");
        await Assert.That(key).DoesNotContain("globalTool=");
        await Assert.That(key).DoesNotContain("toolManifest=");
    }

    private void InstallGlobalTool(string version) =>
        Directory.CreateDirectory(Path.Combine(CliHome, ToolStoreRelativePath, version));

    private void UninstallGlobalTool(string version) =>
        Directory.Delete(Path.Combine(CliHome, ToolStoreRelativePath, version));

    private void WriteToolManifest(string version)
    {
        var configDir = Path.Combine(ProjectDir, ".config");

        var tool = new JsonObject
        {
            ["version"] = version,
            ["commands"] = new JsonArray("metano-typescript"),
        };

        var manifest = new JsonObject
        {
            ["version"] = 1,
            ["isRoot"] = true,
            ["tools"] = new JsonObject { ["metano.compiler.typescript"] = tool },
        };

        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "dotnet-tools.json"), manifest.ToJsonString());
    }

    /// <summary>Runs <c>_MetanoWriteToolchainKey</c> and returns the key file's content.</summary>
    private async Task<string> WriteKey(params string[] extraArguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        string[] arguments =
        [
            "msbuild",
            ProjectFileName,
            "-t:_MetanoWriteToolchainKey",
            "-nologo",
            "-nodeReuse:false",
            "-p:IntermediateOutputPath=obj/",
            $"-p:DOTNET_CLI_HOME={CliHome}",
            .. extraArguments,
        ];

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet could not be started.");

        using var timeout = new CancellationTokenSource(MsBuildTimeout);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);

        await process.WaitForExitAsync(timeout.Token);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"msbuild exited with {process.ExitCode}:\n{await output}\n{await errors}"
            );

        return File.ReadAllText(KeyPath);
    }
}
