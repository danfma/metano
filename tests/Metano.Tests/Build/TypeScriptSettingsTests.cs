using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Metano.Tests.Build;

/// <summary>
/// FR-086 (#328): <c>Metano.Build.TypeScript</c> resolves every setting S as
/// <c>$(MetanoTs&lt;S&gt;)</c>, else <c>$(Metano&lt;S&gt;)</c>, else the default. The tests run the
/// shipped resolve target with a real <c>dotnet msbuild</c> and read the resolved values back.
/// </summary>
public partial class TypeScriptSettingsTests
{
    private const string ProjectFileName = "App.proj";

    /// <summary>The settings list of FR-086.1, the public contract of the package.</summary>
    private static readonly string[] DocumentedSettings =
    [
        "OutputDir",
        "Enabled",
        "Clean",
        "ShowTimings",
        "Dist",
        "PackageRoot",
        "SrcRoot",
        "ImportAlias",
        "SourceCondition",
        "SkipPackageJson",
        "NamespaceBarrels",
        "StripInterfacePrefix",
        "UsingLowering",
        "FilePrefix",
        "ExtraArgs",
        "PostBuildCommand",
        "SkipToolCheck",
        "CompilerProject",
    ];

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-ts-settings-")
        .FullName;

    private static string TargetsPath =>
        Path.Combine(AppContext.BaseDirectory, "MetanoBuild", "Metano.Build.TypeScript.targets");

    private string ProjectDir => Path.Combine(_root, "app");

    [Before(Test)]
    public void CreateProject()
    {
        Directory.CreateDirectory(ProjectDir);

        File.WriteAllText(
            Path.Combine(ProjectDir, ProjectFileName),
            $"""<Project><Import Project="{TargetsPath}" /></Project>"""
        );
    }

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task ResolutionLines_MatchTheDocumentedSettings()
    {
        var targets = File.ReadAllText(TargetsPath);

        var resolved = ResolutionLinePattern()
            .Matches(targets)
            .Select(match => match.Groups["setting"].Value)
            .Distinct()
            .Order();

        await Assert.That(resolved).IsEquivalentTo(DocumentedSettings.Order());
    }

    [Test]
    [Arguments("ImportAlias", "global", "prefixed", "prefixed")]
    [Arguments("ImportAlias", "global", "", "global")]
    [Arguments("Clean", "true", "false", "false")]
    [Arguments("Enabled", "", "", "true")]
    [Arguments("Dist", "", "", "")]
    public async Task PrefixedSetting_OverridesGlobal_ThenDefault(
        string setting,
        string global,
        string prefixed,
        string expected
    )
    {
        var properties = await Resolve(
            $"-p:Metano{setting}={global}",
            $"-p:MetanoTs{setting}={prefixed}"
        );

        await Assert
            .That(properties[$"_MetanoTs{setting}"]?.GetValue<string>())
            .IsEqualTo(expected);
    }

    [Test]
    public async Task OutputDir_EnablesTheTarget_UnlessDisabled()
    {
        var enabled = await Resolve("-p:MetanoTsOutputDir=out");
        var disabled = await Resolve("-p:MetanoOutputDir=out", "-p:MetanoTsEnabled=false");
        var unconfigured = await Resolve();

        await Assert.That(enabled["_MetanoTsActive"]?.GetValue<string>()).IsEqualTo("true");
        await Assert.That(disabled["_MetanoTsActive"]?.GetValue<string>()).IsEqualTo("");
        await Assert.That(unconfigured["_MetanoTsActive"]?.GetValue<string>()).IsEqualTo("");
    }

    /// <summary>FR-086.2: settings are read when the build runs, after every import.</summary>
    [Test]
    public async Task SettingsSetAfterTheImport_AreHonoured()
    {
        File.WriteAllText(
            Path.Combine(ProjectDir, ProjectFileName),
            $"""
            <Project>
              <Import Project="{TargetsPath}" />
              <PropertyGroup><MetanoOutputDir>late</MetanoOutputDir></PropertyGroup>
            </Project>
            """
        );

        var properties = await Resolve();

        await Assert.That(properties["_MetanoTsOutputDir"]?.GetValue<string>()).IsEqualTo("late");
        await Assert.That(properties["_MetanoTsActive"]?.GetValue<string>()).IsEqualTo("true");
    }

    /// <summary>
    /// <c>MetanoTranspileTs</c> run on its own resolves its settings first, so it reaches the
    /// tool probe instead of skipping silently. The tool is absent here, so the probe fails, or,
    /// when a <c>dotnet-metano-typescript</c> is on PATH, the transpile starts.
    /// </summary>
    [Test]
    public async Task MetanoTranspileTs_RunsOnItsOwn()
    {
        string[] arguments = [ProjectFileName, "-t:MetanoTranspileTs", "-p:MetanoOutputDir=out"];

        var run = await MsBuildProcess.Run(ProjectDir, Path.Combine(_root, "home"), arguments);

        var reachedTheTool =
            run.Output.Contains("METANOBUILD001", StringComparison.Ordinal)
            || run.Output.Contains("Metano: Transpiling", StringComparison.Ordinal);

        await Assert.That(reachedTheTool).IsTrue();
    }

    [GeneratedRegex(
        """<_MetanoTs(?<setting>\w+) Condition="'\$\(MetanoTs\k<setting>\)' != ''"\s*>\$\(MetanoTs\k<setting>\)</_MetanoTs\k<setting>>"""
    )]
    private static partial Regex ResolutionLinePattern();

    private async Task<JsonObject> Resolve(params string[] settings)
    {
        string[] queried =
        [
            .. DocumentedSettings.Select(setting => $"_MetanoTs{setting}"),
            "_MetanoTsActive",
        ];

        string[] arguments =
        [
            ProjectFileName,
            "-t:_MetanoResolveTargets",
            $"-getProperty:{string.Join(',', queried)}",
            .. settings,
        ];

        var run = await MsBuildProcess.Run(ProjectDir, Path.Combine(_root, "home"), arguments);

        if (run.ExitCode != 0)
            throw new InvalidOperationException(
                $"msbuild exited with {run.ExitCode}:\n{run.Output}"
            );

        var json = JsonNode.Parse(run.Output)
            ?? throw new InvalidOperationException($"msbuild printed no JSON:\n{run.Output}");

        return json["Properties"]!.AsObject();
    }
}
