namespace Metano.Tests.Build;

/// <summary>
/// Issue #797: the transpile target lists an input list that <c>_MetanoWriteInputLists</c>
/// rewrites only when the set of compiled sources, assets and references changes, so a removed
/// or renamed file reruns the transpile. Each test evaluates the shipped targets with a real
/// <c>dotnet msbuild</c> run against a bare project.
/// </summary>
public class InputListTests
{
    private const string ProjectFileName = "App.proj";

    private readonly string _root = Directory.CreateTempSubdirectory("metano-input-list-").FullName;

    private string ProjectDir => Path.Combine(_root, "app");

    private string CliHome => Path.Combine(_root, "home");

    private string ListPath =>
        Path.Combine(ProjectDir, "obj", $"{ProjectFileName}.MetanoInputs.typescript.cache");

    [Before(Test)]
    public void CreateProject()
    {
        Directory.CreateDirectory(ProjectDir);
        WriteProject(sources: ["A.cs", "B.cs"]);
    }

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task List_NamesSourcesAssetsOfTheTargetLanguageAndReferences()
    {
        WriteProject(
            sources: ["A.cs"],
            items: """
            <MetanoAsset Include="seed.sql" OutputPath="seed.sql" />
            <MetanoAsset Include="widget.dart" OutputPath="widget.dart" Language="dart" />
            <ReferencePath Include="/refs/Lib.dll" />
            """
        );

        var list = await WriteList();

        await Assert.That(list).Contains("A.cs");
        await Assert.That(list).Contains("seed.sql");
        await Assert.That(list).Contains("/refs/Lib.dll");
        await Assert.That(list).DoesNotContain("widget.dart");
    }

    [Test]
    public async Task RemovedSource_ChangesTheList()
    {
        var before = await WriteList();

        WriteProject(sources: ["A.cs"]);
        var after = await WriteList();

        await Assert.That(after).IsNotEqualTo(before);
        await Assert.That(after).DoesNotContain("B.cs");
    }

    [Test]
    public async Task RenamedSource_ChangesTheList()
    {
        var before = await WriteList();

        WriteProject(sources: ["A.cs", "Renamed.cs"]);
        var after = await WriteList();

        await Assert.That(after).IsNotEqualTo(before);
        await Assert.That(after).Contains("Renamed.cs");
    }

    /// <summary>
    /// An unchanged set must leave the list's timestamp alone, or every build would rerun the
    /// transpile target.
    /// </summary>
    [Test]
    public async Task UnchangedSet_KeepsTheListTimestamp()
    {
        await WriteList();
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var past = oneHourAgo.AddTicks(-(oneHourAgo.Ticks % TimeSpan.TicksPerSecond));
        File.SetLastWriteTimeUtc(ListPath, past);

        await WriteList();

        await Assert.That(File.GetLastWriteTimeUtc(ListPath)).IsEqualTo(past);
    }

    private void WriteProject(string[] sources, string items = "")
    {
        var targetsPath = Path.Combine(
            AppContext.BaseDirectory,
            "MetanoBuild",
            "Metano.Build.TypeScript.targets"
        );

        var compile = string.Join(
            "",
            sources.Select(source => $"""<Compile Include="{source}" />""")
        );

        File.WriteAllText(
            Path.Combine(ProjectDir, ProjectFileName),
            $"""
            <Project>
              <Import Project="{targetsPath}" />
              <ItemGroup>{compile}{items}</ItemGroup>
            </Project>
            """
        );
    }

    private async Task<string> WriteList()
    {
        string[] arguments =
        [
            ProjectFileName,
            "-t:_MetanoCollectUserInputs;_MetanoWriteInputLists",
            "-p:MetanoOutputDir=out",
        ];

        var run = await MsBuildProcess.Run(ProjectDir, CliHome, arguments);

        if (run.ExitCode != 0)
            throw new InvalidOperationException(
                $"msbuild exited with {run.ExitCode}:\n{run.Output}"
            );

        return File.ReadAllText(ListPath);
    }
}
