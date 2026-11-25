namespace Metano.Tests.Build;

/// <summary>
/// Issue #785: a build that skips the transpile as up to date reports again the warnings the
/// producing run kept beside the stamp (<c>.metano-diagnostics</c>), and a build whose worker
/// runs does not report them twice. The worker is a stub that writes what the CLI writes.
/// </summary>
public class DiagnosticReplayBuildTests
{
    private const string WarningLine = "Note.cs(3,5): warning MS0001: dropped code";

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-replay-build-")
        .FullName;

    [After(Test)]
    public void DeleteTempDir() => Directory.Delete(_root, recursive: true);

    [Test]
    public async Task UpToDateBuild_ReportsTheKeptWarningAgain()
    {
        WriteProject();

        var first = await Transpile();
        var skipped = await Transpile();

        await Assert.That(first.Output).Contains("WORKER-RAN");
        await Assert.That(CountOf(first.Output, "warning MS0001")).IsEqualTo(0);
        await Assert.That(skipped.Output).DoesNotContain("WORKER-RAN");
        await Assert.That(skipped.Output).Contains("warning MS0001: dropped code");
        await Assert.That(skipped.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task UpToDateBuild_WithNoKeptWarning_ReportsNothing()
    {
        WriteProject(keptWarnings: false);

        await Transpile();
        var skipped = await Transpile();

        await Assert.That(skipped.Output).DoesNotContain("warning MS0001");
        await Assert.That(skipped.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task UpToDateBuild_WithEscalatedWarningsSetting_RunsTheWorkerInsteadOfReplaying()
    {
        WriteProject();

        await Transpile();
        var escalated = await Transpile("-p:TreatWarningsAsErrors=true");

        await Assert.That(escalated.Output).Contains("WORKER-RAN");
    }

    private static int CountOf(string text, string value) => text.Split(value).Length - 1;

    private void WriteProject(bool keptWarnings = true)
    {
        var targets = Path.Combine(
            AppContext.BaseDirectory,
            "MetanoBuild",
            "Metano.Build.TypeScript.targets"
        );

        var diagnosticsLines = keptWarnings
            ? $"""<WriteLinesToFile File="$(_MetanoWorkerOutputDir)/.metano-diagnostics" Lines="{WarningLine}" Overwrite="true" />"""
            : "";

        File.WriteAllText(
            Path.Combine(_root, "App.proj"),
            $"""
            <Project>
              <Import Project="{targets}" />
              <PropertyGroup>
                <MetanoOutputDir>out</MetanoOutputDir>
              </PropertyGroup>
              <Target Name="_MetanoTranspileWorker">
                <MakeDir Directories="$(_MetanoWorkerOutputDir)" />
                {diagnosticsLines}
                <Touch Files="$(_MetanoWorkerOutputDir)/.metano-stamp" AlwaysCreate="true" />
                <Message Importance="high" Text="WORKER-RAN" />
              </Target>
            </Project>
            """
        );
    }

    private Task<MsBuildRun> Transpile(params string[] settings) =>
        MsBuildProcess.Run(
            _root,
            Path.Combine(_root, "home"),
            ["App.proj", "-t:MetanoTranspileTs", .. settings]
        );
}
