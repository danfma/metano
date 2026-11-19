using System.Xml.Linq;

namespace Metano.Tests.Build;

/// <summary>
/// FR-086 / FR-087.2 (#328): several Metano targets in one project. Two synthetic targets
/// (<see cref="SyntheticTargetFile"/>, prefixes <c>A</c> and <c>B</c>) run over the shipped
/// <c>Metano.Build.Common.targets</c> through a real <c>dotnet msbuild</c>, so the tests
/// exercise exactly the registration contract a real per-target package uses.
/// </summary>
public class MultiTargetBuildTests
{
    private const string ProjectFileName = "App.proj";

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-multi-target-")
        .FullName;

    private string ProjectDir => Path.Combine(_root, "app");

    private string CliHome => Path.Combine(_root, "home");

    [Before(Test)]
    public void CopyTargets()
    {
        Directory.CreateDirectory(ProjectDir);

        var shippedTargets = Path.Combine(AppContext.BaseDirectory, "MetanoBuild");

        foreach (var file in Directory.GetFiles(shippedTargets, "*.targets"))
            File.Copy(file, Path.Combine(ProjectDir, Path.GetFileName(file)));

        foreach (var prefix in (string[])["A", "B"])
        {
            File.WriteAllText(
                Path.Combine(ProjectDir, SyntheticTargetFile.FileNameOf(prefix)),
                SyntheticTargetFile.ContentOf(prefix)
            );
        }

        WriteProject();
    }

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task UnconfiguredTargets_StayInert()
    {
        var run = await Transpile();

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).DoesNotContain("Metano: Transpiling");
        await Assert.That(Directory.Exists(Path.Combine(ProjectDir, "obj"))).IsFalse();
    }

    [Test]
    public async Task TwoTargets_EachRunWithItsOwnSettingsAndStamp()
    {
        var run = await Transpile(
            "-p:MetanoFakeAOutputDir=outA",
            "-p:MetanoFakeBOutputDir=outB",
            "-p:MetanoFilePrefix=global",
            "-p:MetanoFakeBFilePrefix=only-b"
        );

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).Contains("Metano: Transpiling App → outA");
        await Assert.That(run.Output).Contains("Metano: Transpiling App → outB");
        await Assert.That(run.Output).Contains("FAKE-A [global]");
        await Assert.That(run.Output).Contains("FAKE-B [only-b]");

        await Assert
            .That(run.Output)
            .DoesNotContain("different versions of Metano.Build.Common.targets");

        await Assert.That(File.Exists(StampOf("outA"))).IsTrue();
        await Assert.That(File.Exists(StampOf("outB"))).IsTrue();
    }

    [Test]
    public async Task UpToDateTargets_AreSkipped_AndOnlyTheStaleOneReruns()
    {
        string[] settings = ["-p:MetanoFakeAOutputDir=outA", "-p:MetanoFakeBOutputDir=outB"];
        await Transpile(settings);

        var upToDate = await Transpile(settings);
        File.Delete(StampOf("outA"));
        var afterDeletingA = await Transpile(settings);

        await Assert.That(upToDate.Output).DoesNotContain("Metano: Transpiling");
        await Assert.That(afterDeletingA.Output).Contains("Metano: Transpiling App → outA");
        await Assert.That(afterDeletingA.Output).DoesNotContain("Metano: Transpiling App → outB");
    }

    [Test]
    public async Task Assets_ReachOnlyTheTargetOfTheirLanguage()
    {
        WriteProject(
            $"""<ItemGroup><MetanoAsset Include="{ProjectFileName}" Language="{SyntheticTargetFile.LanguageOf(
                "A"
            )}" /></ItemGroup>"""
        );

        var run = await Transpile("-p:MetanoFakeAOutputDir=outA", "-p:MetanoFakeBOutputDir=outB");

        await Assert.That(run.Output).Contains("FAKE-A [] --asset");
        await Assert.That(run.Output).DoesNotContain("FAKE-B [] --asset");
    }

    [Test]
    [Arguments("-p:MetanoOutputDir=out", "-p:MetanoOutputDir=out")]
    [Arguments("-p:MetanoFakeAOutputDir=out/", "-p:MetanoFakeBOutputDir=./OUT")]
    public async Task SameOutputDirectory_FailsWithMetanoBuild002(string settingA, string settingB)
    {
        var run = await Transpile(settingA, settingB);

        await Assert.That(run.ExitCode).IsNotEqualTo(0);
        await Assert.That(run.Output).Contains("METANOBUILD002");
        await Assert.That(run.Output).Contains("fake-A, fake-B");
        await Assert.That(run.Output).DoesNotContain("Metano: Transpiling");
    }

    [Test]
    public async Task DisabledTarget_RunsNeitherTranspileNorPostBuild()
    {
        var run = await Transpile(
            "-p:MetanoFakeAOutputDir=outA",
            "-p:MetanoFakeBOutputDir=outB",
            "-p:MetanoFakeBEnabled=false",
            "-p:MetanoPostBuildCommand=echo POST-GLOBAL"
        );

        await Assert.That(run.Output).Contains("Metano: Transpiling App → outA");
        await Assert.That(run.Output).DoesNotContain("Metano: Transpiling App → outB");
        await Assert.That(CountOf("POST-GLOBAL", run.Output)).IsEqualTo(1);
    }

    [Test]
    public async Task PostBuildCommand_RunsOnEveryBuild_PerEnabledTarget()
    {
        string[] settings =
        [
            "-p:MetanoFakeAOutputDir=outA",
            "-p:MetanoFakeBOutputDir=outB",
            "-p:MetanoPostBuildCommand=echo POST-GLOBAL",
            "-p:MetanoFakeBPostBuildCommand=echo POST-B",
        ];

        await Transpile(settings);

        var upToDate = await Transpile(settings);

        await Assert.That(upToDate.Output).DoesNotContain("Metano: Transpiling");
        await Assert.That(CountOf("POST-GLOBAL", upToDate.Output)).IsEqualTo(1);
        await Assert.That(CountOf("POST-B", upToDate.Output)).IsEqualTo(1);
    }

    [Test]
    public async Task BeforeAndAfterHooks_KeepTheirPlaceAroundEveryTarget()
    {
        WriteProject(
            """
            <Target Name="UserBefore" BeforeTargets="MetanoTranspile"><Message Importance="high" Text="HOOK-BEFORE" /></Target>
            <Target Name="UserAfter" AfterTargets="MetanoTranspile"><Message Importance="high" Text="HOOK-AFTER" /></Target>
            """
        );

        var run = await Transpile("-p:MetanoFakeAOutputDir=outA", "-p:MetanoFakeBOutputDir=outB");

        var before = run.Output.IndexOf("HOOK-BEFORE", StringComparison.Ordinal);
        var firstTranspile = run.Output.IndexOf("Metano: Transpiling", StringComparison.Ordinal);
        var lastTranspile = run.Output.LastIndexOf("Metano: Transpiling", StringComparison.Ordinal);
        var after = run.Output.IndexOf("HOOK-AFTER", StringComparison.Ordinal);

        await Assert.That(before).IsGreaterThanOrEqualTo(0);
        await Assert.That(before).IsLessThan(firstTranspile);
        await Assert.That(lastTranspile).IsLessThan(after);
    }

    /// <summary>
    /// As in 2.x, an asset a target adds before <c>MetanoTranspile</c> reaches the tool: the
    /// asset arguments are composed in the project instance, not in the worker.
    /// </summary>
    [Test]
    public async Task AssetAddedByAHook_ReachesTheTool()
    {
        WriteProject(
            $$"""
            <Target Name="AddSeed" BeforeTargets="MetanoTranspile">
              <ItemGroup>
                <MetanoAsset Include="seed.sql" OutputPath="db/seed.sql" Language="{{SyntheticTargetFile.LanguageOf(
                "A"
            )}}" />
              </ItemGroup>
            </Target>
            """
        );

        var run = await Transpile("-p:MetanoFakeAOutputDir=outA", "-p:MetanoFakeBOutputDir=outB");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).Contains("seed.sql=db/seed.sql");
        await Assert.That(run.Output).DoesNotContain("FAKE-B [] --asset");
    }

    /// <summary>
    /// As in 2.x, a setting a <c>BeforeTargets="MetanoTranspile"</c> hook sets reaches the tool,
    /// and the next build is still skipped.
    /// </summary>
    [Test]
    public async Task SettingSetByAHook_ReachesTheTool()
    {
        WriteProject(
            """
            <Target Name="ComputePrefix" BeforeTargets="MetanoTranspile">
              <PropertyGroup><MetanoFilePrefix>from-hook</MetanoFilePrefix></PropertyGroup>
            </Target>
            """
        );

        var first = await Transpile("-p:MetanoFakeAOutputDir=outA");
        var second = await Transpile("-p:MetanoFakeAOutputDir=outA");

        await Assert.That(first.Output).Contains("FAKE-A [from-hook]");
        await Assert.That(second.Output).DoesNotContain("Metano: Transpiling");
    }

    [Test]
    public async Task PerTargetTarget_RunsOnItsOwn()
    {
        var run = await MsBuildProcess.Run(
            ProjectDir,
            CliHome,
            [
                ProjectFileName,
                "-t:MetanoTranspileFakeA",
                "-p:MetanoFakeAOutputDir=outA",
                "-p:MetanoFakeBOutputDir=outB",
            ]
        );

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).Contains("Metano: Transpiling App → outA");
        await Assert.That(run.Output).DoesNotContain("Metano: Transpiling App → outB");
    }

    /// <summary>
    /// A multi-line <c>MetanoFilePrefix</c> with a <c>;</c> reaches the worker instance intact
    /// through <c>MSBuild::Escape</c> and the global properties of the <c>MSBuild</c> task.
    /// </summary>
    [Test]
    public async Task MultiLineFilePrefix_ReachesTheWorkerIntact()
    {
        WriteProject(
            """
            <PropertyGroup>
              <MetanoFilePrefix>first line;
            second line</MetanoFilePrefix>
            </PropertyGroup>
            """
        );

        var run = await Transpile("-p:MetanoFakeAOutputDir=outA");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).Contains("FAKE-A [first line;");
        await Assert.That(run.Output).Contains("second line]");
    }

    [Test]
    [Arguments(false, true, "", true)]
    [Arguments(true, true, "", true)]
    [Arguments(false, false, "", false)]
    [Arguments(true, false, "", false)]
    [Arguments(false, true, "METANOBUILD003", false)]
    [Arguments(true, true, "METANOBUILD003", false)]
    [Arguments(false, true, "OTHER%3Bmetanobuild003", false)]
    [Arguments(false, true, "OTHER%2Cmetanobuild003%2CANOTHER", false)]
    [Arguments(true, true, "OTHER%2CMETANOBUILD003%2CANOTHER", false)]
    [Arguments(false, true, "METANOBUILD0030", true)]
    public async Task CommonTargetCopies_WarnOnlyOnUnsuppressedContentSkew(
        bool otherFirst,
        bool skew,
        string noWarn,
        bool expectedWarning
    )
    {
        var otherPackage = Directory
            .CreateDirectory(Path.Combine(ProjectDir, "other-package"))
            .FullName;

        var common = File.ReadAllText(BuildCommonTargetsFixture.PathIn(ProjectDir));

        File.WriteAllText(
            BuildCommonTargetsFixture.PathIn(otherPackage),
            skew ? common + "\n<!-- other release -->\n" : common
        );

        File.WriteAllText(
            Path.Combine(otherPackage, SyntheticTargetFile.FileNameOf("B")),
            SyntheticTargetFile.ContentOf("B")
        );

        var projectPath = Path.Combine(ProjectDir, ProjectFileName);
        var project = File.ReadAllText(projectPath);

        File.WriteAllText(
            projectPath,
            otherFirst
                ? $"""
                <Project>
                  <Import Project="other-package/{SyntheticTargetFile.FileNameOf("B")}" />
                  <Import Project="{SyntheticTargetFile.FileNameOf("A")}" />
                </Project>
                """
                : project.Replace(
                    SyntheticTargetFile.FileNameOf("B"),
                    $"other-package/{SyntheticTargetFile.FileNameOf("B")}"
                )
        );

        var run = await Transpile(
            "-p:MetanoFakeAOutputDir=outA",
            "-p:MetanoFakeBOutputDir=outB",
            $"-p:NoWarn={noWarn}"
        );

        await Assert.That(run.ExitCode).IsEqualTo(0);

        await Assert
            .That(run.Output.Contains("warning METANOBUILD003:", StringComparison.Ordinal))
            .IsEqualTo(expectedWarning);

        await Assert.That(run.Output).Contains("FAKE-A");
        await Assert.That(run.Output).Contains("FAKE-B");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreContractPackage_DoesNotProduceASpeculativeSkewWarning(bool olderFirst)
    {
        var olderPackage = Directory
            .CreateDirectory(Path.Combine(ProjectDir, "older-package"))
            .FullName;

        // Model a package predating the registration/check contract. Its shared targets
        // deliberately differ, but no reliable comparison is available in either order.
        BuildCommonTargetsFixture
            .PreContractCopy(ProjectDir)
            .Save(BuildCommonTargetsFixture.PathIn(olderPackage));

        var olderTarget = XDocument.Parse(SyntheticTargetFile.ContentOf("B"));
        olderTarget.Root!.Elements("ItemGroup").Elements("_MetanoBuildCommonCopy").Remove();
        olderTarget.Save(Path.Combine(olderPackage, SyntheticTargetFile.FileNameOf("B")));
        var newerImport = $"<Import Project=\"{SyntheticTargetFile.FileNameOf("A")}\" />";

        var olderImport =
            $"<Import Project=\"older-package/{SyntheticTargetFile.FileNameOf("B")}\" />";

        File.WriteAllText(
            Path.Combine(ProjectDir, ProjectFileName),
            olderFirst
                ? $"<Project>{olderImport}{newerImport}</Project>"
                : $"<Project>{newerImport}{olderImport}</Project>"
        );

        var run = await Transpile("-p:MetanoFakeAOutputDir=outA", "-p:MetanoFakeBOutputDir=outB");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).DoesNotContain("warning");
        await Assert.That(run.Output).Contains("FAKE-A");
        await Assert.That(run.Output).Contains("FAKE-B");
    }

    private void WriteProject(string body = "")
    {
        var content = $"""
            <Project>
              <Import Project="{SyntheticTargetFile.FileNameOf("A")}" />
              <Import Project="{SyntheticTargetFile.FileNameOf("B")}" />
              {body}
            </Project>
            """;

        File.WriteAllText(Path.Combine(ProjectDir, ProjectFileName), content);
    }

    private string StampOf(string outputDir) =>
        Path.Combine(ProjectDir, outputDir, ".metano-stamp");

    private static int CountOf(string text, string output) => output.Split(text).Length - 1;

    private Task<MsBuildRun> Transpile(params string[] settings) =>
        MsBuildProcess.Run(
            ProjectDir,
            CliHome,
            [ProjectFileName, "-t:MetanoTranspile", .. settings]
        );
}
