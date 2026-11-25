using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.TypeScript.AST;
using Metano.Packaging;
using Microsoft.CodeAnalysis;

namespace Metano.Tests;

/// <summary>
/// #764: a <c>package.json</c> that declares <c>workspaces</c> is never a package root.
/// #765: the <c>version</c> a generated package.json carries and the dependency range a
/// consumer writes come from one source, so a workspace satisfies the range.
/// </summary>
public class PackageWorkspaceTests
{
    private const string WorkspaceRootJson = """{ "name": "ws", "workspaces": ["targets/js/*"] }""";

    private readonly string _root = Directory.CreateTempSubdirectory("metano-workspace-").FullName;

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── #764: workspace roots ──────────────────────────────────────────────

    [Test]
    public async Task WorkspaceRoot_IsNeverAdopted_PackageGetsItsOwnPackageJson()
    {
        var rootPackageJson = CreateWorkspace();
        var before = File.ReadAllBytes(rootPackageJson);
        var output = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "src");
        var project = Path.Combine(_root, "ws", "src", "B", "B.csproj");

        var resolution = PackageRootResolver.Resolve(output, project);
        var reconciliation = Reconcile(resolution, output, packageName: "pkg-b");

        var packageJson = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "package.json");

        await Assert.That(resolution.Root).IsEqualTo(Path.GetDirectoryName(output));
        await Assert.That(reconciliation.WasWritten).IsTrue();
        await Assert.That(reconciliation.Diagnostics.Any(d => d.Code == "MS0007")).IsFalse();
        await Assert.That(File.ReadAllBytes(rootPackageJson).SequenceEqual(before)).IsTrue();
        await Assert.That(Read(packageJson)["name"]?.GetValue<string>()).IsEqualTo("pkg-b");
    }

    [Test]
    public async Task WorkspaceRootAsTheDefaultPackageRoot_IsRefused()
    {
        var rootPackageJson = CreateWorkspace();
        var before = File.ReadAllBytes(rootPackageJson);
        var output = Path.Combine(_root, "ws", "src");
        var project = Path.Combine(_root, "ws", "App.csproj");

        var resolution = PackageRootResolver.Resolve(output, project);
        var reconciliation = Reconcile(resolution, output, packageName: "app");

        await Assert.That(resolution.IsRefused).IsTrue();
        await Assert.That(reconciliation.WasWritten).IsFalse();
        await Assert.That(File.ReadAllBytes(rootPackageJson).SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task PackageJsonWithoutWorkspaces_IsStillAdopted()
    {
        var rootPackageJson = CreateWorkspace("""{ "name": "plain" }""");
        var output = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "src");
        var project = Path.Combine(_root, "ws", "src", "B", "B.csproj");

        var resolution = PackageRootResolver.Resolve(output, project);

        await Assert.That(resolution.Root).IsEqualTo(Path.GetDirectoryName(rootPackageJson));
    }

    [Test]
    public async Task PnpmWorkspaceFile_MakesThePackageJsonBesideItAWorkspaceRoot()
    {
        var rootPackageJson = CreateWorkspace("""{ "name": "plain" }""");

        File.WriteAllText(
            Path.Combine(_root, "ws", "pnpm-workspace.yaml"),
            "packages:\n  - 'targets/js/*'\n"
        );

        var before = File.ReadAllBytes(rootPackageJson);
        var output = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "src");
        var project = Path.Combine(_root, "ws", "src", "B", "B.csproj");

        var resolution = PackageRootResolver.Resolve(output, project);
        Reconcile(resolution, output, packageName: "pkg-b");

        await Assert.That(resolution.Root).IsEqualTo(Path.GetDirectoryName(output));
        await Assert.That(File.ReadAllBytes(rootPackageJson).SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task WorkspacesObjectForm_IsAWorkspaceRoot()
    {
        CreateWorkspace("""{ "name": "ws", "workspaces": { "packages": ["targets/js/*"] } }""");

        var output = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "src");
        var project = Path.Combine(_root, "ws", "src", "B", "B.csproj");

        var resolution = PackageRootResolver.Resolve(output, project);

        await Assert.That(resolution.Root).IsEqualTo(Path.GetDirectoryName(output));
    }

    [Test]
    public async Task ExplicitPackageRoot_UnderAWorkspaceRoot_IsUsedAsGiven()
    {
        CreateWorkspace();

        var explicitRoot = Path.Combine(_root, "ws");
        var output = Path.Combine(_root, "ws", "targets", "js", "pkg-b", "src");
        var project = Path.Combine(_root, "ws", "src", "B", "B.csproj");

        var resolution = PackageRootResolver.Resolve(output, project, explicitRoot);

        await Assert.That(resolution.Root).IsEqualTo(explicitRoot);
    }

    [Test]
    public async Task WorkspaceRootFallback_NamedLikeThePackage_IsAdopted()
    {
        CreateWorkspace("""{ "name": "my-lib", "workspaces": ["examples/*"] }""");

        var output = Path.Combine(_root, "ws", "src");
        var project = Path.Combine(_root, "ws", "App.csproj");

        var resolution = PackageRootResolver.Resolve(output, project, packageName: "my-lib");

        await Assert.That(resolution.Root).IsEqualTo(Path.Combine(_root, "ws"));
    }

    [Test]
    public async Task WorkspaceRootFallback_WithAnotherName_IsRefusedNamingTheCause()
    {
        CreateWorkspace("""{ "name": "ws", "workspaces": ["examples/*"] }""");

        var output = Path.Combine(_root, "ws", "src");
        var project = Path.Combine(_root, "ws", "App.csproj");

        var resolution = PackageRootResolver.Resolve(output, project, packageName: "my-lib");

        await Assert.That(resolution.IsRefused).IsTrue();
        await Assert.That(resolution.Refusal!.Message).Contains("workspace root");
    }

    // ── #765: one version rule ─────────────────────────────────────────────

    [Test]
    [Arguments(
        "[assembly: System.Reflection.AssemblyInformationalVersion(\"1.5.2-beta.1+abc123\")]",
        ""
    )]
    [Arguments("[assembly: System.Reflection.AssemblyVersion(\"1.5.2.0\")]", "")]
    [Arguments("", ", Version = \"0.1.0\"")]
    [Arguments("", ", Version = \"^2.1.0\"")]
    [Arguments("", ", Version = \"workspace:*\"")]
    public async Task DependencyRange_IsSatisfiedByTheDependencyPackageVersion(
        string versionAttribute,
        string emitPackageArguments
    )
    {
        var library = $$"""
            {{versionAttribute}}
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("pkg-a"{{emitPackageArguments}})]

            namespace A;

            public record Item(string Name);
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace B;

            public class Holder
            {
                public A.Item? Stored { get; set; }
            }
            """;

        var libCompilation = TranspileHelper.CompileLibrary(library);

        var transformer = TranspileHelper.NewTransformer(
            TranspileHelper.CompileConsumer(consumer, libCompilation)
        );

        transformer.TransformAll();

        var range = transformer.CrossPackageDependencies["pkg-a"];

        var explicitVersion =
            emitPackageArguments.Length > 0 ? emitPackageArguments.Split('"')[1] : null;

        var version = RoslynTypeQueries.ResolveOwnPackageVersion(
            libCompilation.Assembly,
            explicitVersion
        );

        await Assert.That(Satisfies(range, version)).IsTrue();
    }

    [Test]
    [Arguments("<2.0.0")]
    [Arguments(">1.0.0")]
    [Arguments(">=1.0.0 <2.0.0")]
    [Arguments("workspace:*")]
    public async Task ExplicitVersionWithoutABaseVersion_FallsBackToTheAssemblyVersion(
        string explicitVersion
    )
    {
        var compilation = TranspileHelper.CompileLibrary(
            """
            [assembly: System.Reflection.AssemblyVersion("1.5.2.0")]
            [assembly: TranspileAssembly]
            """
        );

        await Assert
            .That(RoslynTypeQueries.TryParseExplicitVersion(explicitVersion, out _))
            .IsFalse();

        await Assert
            .That(RoslynTypeQueries.ResolveOwnPackageVersion(compilation.Assembly, explicitVersion))
            .IsEqualTo("1.5.2");
    }

    [Test]
    [Arguments("^1.2.3", "1.2.3")]
    [Arguments("~1.2.3", "1.2.3")]
    [Arguments(">=1.2.3", "1.2.3")]
    [Arguments("=1.2.3", "1.2.3")]
    [Arguments("v1.2.3", "1.2.3")]
    [Arguments("1.2.3-rc.1", "1.2.3-rc.1")]
    public async Task ExplicitVersionWithABaseVersion_WritesTheBaseVersion(
        string explicitVersion,
        string expected
    )
    {
        await Assert
            .That(RoslynTypeQueries.TryParseExplicitVersion(explicitVersion, out var version))
            .IsTrue();

        await Assert.That(version).IsEqualTo(expected);
    }

    [Test]
    public async Task Writer_ReplacingADifferentVersion_ReportsAnInfoDiagnostic()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "info")).FullName;
        var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;

        File.WriteAllText(
            Path.Combine(dir, "package.json"),
            """{ "name": "pkg-a", "version": "0.1.0" }"""
        );

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        var replaced = PackageJsonWriter.UpdateOrCreate(
            dir,
            src,
            files,
            authoritativePackageName: "pkg-a",
            packageVersion: "1.5.2"
        );

        var unchanged = PackageJsonWriter.UpdateOrCreate(
            dir,
            src,
            files,
            authoritativePackageName: "pkg-a",
            packageVersion: "1.5.2"
        );

        await Assert
            .That(
                replaced.Count(d =>
                    d.Severity == Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Info
                )
            )
            .IsEqualTo(1);

        await Assert.That(unchanged).IsEmpty();
    }

    [Test]
    public async Task Writer_PrunesNoUnaliasedImport_WhenItCarriesAConditionOfTheConsumer()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "extended")).FullName;
        var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;

        File.WriteAllText(
            Path.Combine(dir, "package.json"),
            """
            { "name": "pkg", "imports": { "#/*": {
              "types": "./dist/*.d.ts", "import": "./dist/*.js", "default": "./src/*.ts",
              "development": "./dev/*.ts" } } }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            dir,
            src,
            [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "pkg"
        );

        var imports = Read(Path.Combine(dir, "package.json"))["imports"] as JsonObject;

        await Assert.That(imports!.ContainsKey("#/*")).IsTrue();
    }

    [Test]
    public async Task InformationalVersion_DropsBuildMetadata()
    {
        var compilation = TranspileHelper.CompileLibrary(
            """
            [assembly: System.Reflection.AssemblyInformationalVersion("3.4.5-rc.2+sha.deadbeef")]
            [assembly: TranspileAssembly]
            """
        );

        await Assert
            .That(RoslynTypeQueries.ReadPackageVersion(compilation.Assembly))
            .IsEqualTo("3.4.5-rc.2");

        await Assert
            .That(RoslynTypeQueries.FormatAssemblyVersion(compilation.Assembly))
            .IsEqualTo("^3.4.5-rc.2");
    }

    [Test]
    public async Task Writer_AlwaysWritesVersion_ReplacingAHandWrittenOne()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "pkg")).FullName;
        var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;

        File.WriteAllText(
            Path.Combine(dir, "package.json"),
            """{ "name": "pkg-a", "version": "0.1.0" }"""
        );

        PackageJsonWriter.UpdateOrCreate(
            dir,
            src,
            [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "pkg-a",
            packageVersion: "1.5.2"
        );

        await Assert
            .That(Read(Path.Combine(dir, "package.json"))["version"]?.GetValue<string>())
            .IsEqualTo("1.5.2");
    }

    [Test]
    public async Task Writer_NewPackageJson_PlacesVersionRightAfterName()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "fresh")).FullName;

        PackageJsonWriter.UpdateOrCreate(
            dir,
            Path.Combine(dir, "src"),
            [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "pkg-a",
            packageVersion: "1.0.0"
        );

        var keys = Read(Path.Combine(dir, "package.json")).Select(pair => pair.Key).ToList();

        await Assert.That(keys.Take(2)).IsEquivalentTo(["name", "version"]);
    }

    [Test]
    public async Task Writer_KeepsARetargetedUnaliasedImport()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "retargeted")).FullName;
        var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;

        File.WriteAllText(
            Path.Combine(dir, "package.json"),
            """{ "name": "pkg", "imports": { "#/*": { "default": "./lib/*.ts" } } }"""
        );

        PackageJsonWriter.UpdateOrCreate(
            dir,
            src,
            [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "pkg"
        );

        var imports = Read(Path.Combine(dir, "package.json"))["imports"] as JsonObject;

        await Assert.That(imports!.ContainsKey("#/*")).IsTrue();
    }

    private string CreateWorkspace(string rootJson = WorkspaceRootJson)
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "ws")).FullName;

        Directory.CreateDirectory(Path.Combine(workspace, ".git"));

        var packageJson = Path.Combine(workspace, "package.json");

        File.WriteAllText(packageJson, rootJson);

        return packageJson;
    }

    private static PackageJsonReconciliation Reconcile(
        PackageRootResolution resolution,
        string outputDir,
        string packageName
    ) =>
        PackageJsonReconciler.Reconcile(
            PackageManifestInputs.None with
            {
                SourceFiles = [new TsSourceFile("index.ts", [], "")],
                PackageName = packageName,
                PackageVersion = "1.0.0",
            },
            resolution,
            outputDir,
            dist: "./dist",
            srcRoot: null,
            importAlias: null
        );

    private static JsonObject Read(string path) =>
        (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)!;

    /// <summary>Whether <paramref name="version"/> satisfies the exact, caret or workspace range.</summary>
    private static bool Satisfies(string range, string version)
    {
        if (range.StartsWith("workspace:", StringComparison.Ordinal))
            return true;

        if (!range.StartsWith('^'))
            return range == version;

        var baseVersion = range[1..];

        return baseVersion == version
            || (
                Version.TryParse(version.Split('-')[0], out var actual)
                && Version.TryParse(baseVersion.Split('-')[0], out var minimum)
                && actual.Major == minimum.Major
                && actual >= minimum
                && !version.Contains('-')
            );
    }
}
