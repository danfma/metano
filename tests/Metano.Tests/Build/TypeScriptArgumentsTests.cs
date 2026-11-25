namespace Metano.Tests.Build;

/// <summary>Issue #706: the real TypeScript item composition and MSBuild worker boundary preserve argument values.</summary>
public class TypeScriptArgumentsTests
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-ts-arguments-")
        .FullName;

    [After(Test)]
    public void DeleteTempDir() => Directory.Delete(_root, recursive: true);

    [Test]
    [Arguments("Metano")]
    [Arguments("MetanoTs")]
    public async Task Semicolons_ReachTheWorkerWithoutItemSplitting(string prefix)
    {
        WriteProject(
            $"""
            <{prefix}FilePrefix>// first;second %253B
            // third</{prefix}FilePrefix>
            <{prefix}ExtraArgs>--dist &quot;first;second&quot;</{prefix}ExtraArgs>
            """
        );

        var run = await Transpile();
        var arguments = File.ReadAllText(Path.Combine(_root, "arguments.txt"));

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(arguments).Contains("--file-prefix \"// first;second %3B\n// third\"");
        await Assert.That(arguments).Contains("--dist \"first;second\"");
    }

    [Test]
    public async Task WarningSettings_ReachTheWorker_AsCommaSeparatedMetanoFlags()
    {
        WriteProject(
            """
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <WarningsAsErrors>$(WarningsAsErrors);MS0001;nullable</WarningsAsErrors>
            <WarningsNotAsErrors>MS0002</WarningsNotAsErrors>
            <NoWarn>$(NoWarn);MS0003;1591</NoWarn>
            """
        );

        var run = await Transpile();
        var arguments = File.ReadAllText(Path.Combine(_root, "arguments.txt"));

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(arguments).Contains("--warn-as-error");
        await Assert.That(arguments).Contains("--warnings-as-errors \"MS0001,nullable\"");
        await Assert.That(arguments).Contains("--warnings-not-as-errors \"MS0002\"");
        await Assert.That(arguments).Contains("--no-warn \"MS0003,1591\"");
    }

    [Test]
    public async Task WithoutWarningSettings_NoWarningFlagIsPassed()
    {
        WriteProject("");

        await Transpile();
        var arguments = File.ReadAllText(Path.Combine(_root, "arguments.txt"));

        await Assert.That(arguments).DoesNotContain("--warn");
        await Assert.That(arguments).DoesNotContain("--no-warn");
    }

    [Test]
    public async Task ChangingAWarningSetting_RerunsTheWorker()
    {
        WriteProject("");

        await Transpile();
        var unchanged = await Transpile();
        var escalated = await Transpile("-p:MSBuildTreatWarningsAsErrors=true");

        await Assert.That(unchanged.Output).DoesNotContain("WORKER-RAN");
        await Assert.That(escalated.Output).Contains("WORKER-RAN");
    }

    [Test]
    public async Task ChangedArguments_Rerun_AndUnchangedArgumentsSkipTheWorker()
    {
        WriteProject("");
        var first = await Transpile("-p:MetanoFilePrefix=one%3Btwo");
        var unchanged = await Transpile("-p:MetanoFilePrefix=one%3Btwo");
        var changed = await Transpile("-p:MetanoFilePrefix=three%3Bfour");
        var unchangedAgain = await Transpile("-p:MetanoFilePrefix=three%3Bfour");

        await Assert.That(first.Output).Contains("WORKER-RAN");
        await Assert.That(unchanged.Output).DoesNotContain("WORKER-RAN");
        await Assert.That(changed.Output).Contains("WORKER-RAN");
        await Assert.That(unchangedAgain.Output).DoesNotContain("WORKER-RAN");

        await Assert
            .That(File.ReadAllText(Path.Combine(_root, "arguments.txt")))
            .Contains("three;four");
    }

    [Test]
    public async Task OlderCommonWithoutCompositionHook_StillPassesArgumentsToTheWorker()
    {
        WriteProject("<MetanoFilePrefix>older;common</MetanoFilePrefix>");

        var common = BuildCommonTargetsFixture.PreContractCopy(
            Path.Combine(AppContext.BaseDirectory, "MetanoBuild"),
            withoutCompositionHook: true
        );

        var commonPath = Path.Combine(_root, "older-common.targets");
        common.Save(commonPath);
        var projectPath = Path.Combine(_root, "App.proj");
        var project = File.ReadAllText(projectPath);

        File.WriteAllText(
            projectPath,
            project.Replace("<Project>", $"<Project><Import Project=\"{commonPath}\" />")
        );

        var run = await Transpile();

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).DoesNotContain("warning");

        await Assert
            .That(File.ReadAllText(Path.Combine(_root, "arguments.txt")))
            .Contains("--file-prefix \"older;common\"");
    }

    private void WriteProject(string settings)
    {
        var targets = Path.Combine(
            AppContext.BaseDirectory,
            "MetanoBuild",
            "Metano.Build.TypeScript.targets"
        );

        File.WriteAllText(
            Path.Combine(_root, "App.proj"),
            $"""
            <Project>
              <Import Project="{targets}" />
              <PropertyGroup>
                <MetanoOutputDir>out</MetanoOutputDir>
                {settings}
              </PropertyGroup>
              <Target Name="_MetanoTranspileWorker">
                <WriteLinesToFile File="arguments.txt" Lines="$([MSBuild]::Escape('$(_MetanoWorkerArguments)'))" Overwrite="true" />
                <MakeDir Directories="$(_MetanoWorkerOutputDir)" />
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
