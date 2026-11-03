using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.IR;

namespace Metano.Tests;

/// <summary>
/// The generated package.json declares <c>metano-runtime</c> as a peer dependency when an
/// emitted file imports it, without ever overriding what the consumer already declared (#411).
/// </summary>
public class RuntimePeerDependencyTests
{
    private const string Specifier = "^9.8.7";

    private const string RuntimeName = "metano-runtime";

    private static readonly string[] DependencySections =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionalDependencies",
    ];

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task RuntimeImport_AddsPeerDependency()
    {
        var root = CreateTempDir();

        WritePackageJson(root, Specifier);

        var pkg = ReadJson(root);

        await Assert.That(DeclaredVersion(pkg, "peerDependencies")).IsEqualTo(Specifier);
        await Assert.That(pkg["dependencies"]).IsNull();
    }

    [Test]
    public async Task NoRuntimeImport_AddsNothing()
    {
        var root = CreateTempDir();

        WritePackageJson(root, runtimePeerSpecifier: null);

        await Assert.That(ReadJson(root).ContainsKey("peerDependencies")).IsFalse();
    }

    [Test]
    public async Task OwnPackageIsTheRuntime_AddsNothing()
    {
        var root = CreateTempDir();

        WritePackageJson(root, Specifier, packageName: RuntimeName);

        await Assert.That(ReadJson(root).ContainsKey("peerDependencies")).IsFalse();
    }

    [Test]
    [Arguments("""import { HashCode } from "metano-runtime";""", true)]
    [Arguments("""import { type TypeSpec } from "metano-runtime/system/json";""", true)]
    [Arguments("""import type { Grouping } from "metano-runtime";""", true)]
    [Arguments("""import { Decimal } from "decimal.js";""", false)]
    [Arguments("""import { x } from "metano-runtime-like";""", false)]
    [Arguments("""const text = "from metano-runtime";""", false)]
    public async Task IsImportedBy_DetectsRuntimeImportsInPrintedFiles(string line, bool expected)
    {
        var files = new[] { new GeneratedFile("a.ts", $"{line}\n\nexport const a = 1;\n") };

        await Assert.That(RuntimePeerDependency.IsImportedBy(files)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("dependencies", "workspace:*")]
    [Arguments("devDependencies", "^1.0.0")]
    [Arguments("optionalDependencies", "^1.0.0")]
    [Arguments("peerDependencies", ">=2.0.0")]
    public async Task AlreadyDeclared_IsLeftUntouchedWithoutDuplicate(
        string section,
        string declared
    )
    {
        var root = CreateTempDir();

        WriteManifest(
            root,
            $$"""{ "name": "consumer", "{{section}}": { "metano-runtime": "{{declared}}" } }"""
        );

        WritePackageJson(root, Specifier);

        var pkg = ReadJson(root);

        var sectionsDeclaring = DependencySections.Count(name =>
            (pkg[name] as JsonObject)?.ContainsKey(RuntimeName) == true
        );

        await Assert.That(DeclaredVersion(pkg, section)).IsEqualTo(declared);
        await Assert.That(sectionsDeclaring).IsEqualTo(1);
    }

    [Test]
    [Arguments("^2.7.3")]
    [Arguments("*")]
    public async Task PeerEntryOfTheWritersOwnShape_IsRefreshed(string stored)
    {
        var root = CreateTempDir();

        WriteManifest(root, PeerManifest(stored));
        WritePackageJson(root, Specifier);

        await Assert.That(DeclaredVersion(ReadJson(root), "peerDependencies")).IsEqualTo(Specifier);
    }

    [Test]
    [Arguments("workspace:*")]
    [Arguments("~2.7")]
    [Arguments(">=2")]
    public async Task PeerEntryOfAnotherShape_IsKept(string stored)
    {
        var root = CreateTempDir();

        WriteManifest(root, PeerManifest(stored));
        WritePackageJson(root, Specifier);

        await Assert.That(DeclaredVersion(ReadJson(root), "peerDependencies")).IsEqualTo(stored);
    }

    [Test]
    public async Task OwnShapedPeerEntry_IsKeptWhenAnotherSectionDeclaresTheRuntime()
    {
        var root = CreateTempDir();

        WriteManifest(
            root,
            """
            {
              "name": "consumer",
              "dependencies": { "metano-runtime": "workspace:*" },
              "peerDependencies": { "metano-runtime": "^2.7.3" }
            }
            """
        );

        WritePackageJson(root, Specifier);

        var pkg = ReadJson(root);

        await Assert.That(DeclaredVersion(pkg, "dependencies")).IsEqualTo("workspace:*");
        await Assert.That(DeclaredVersion(pkg, "peerDependencies")).IsEqualTo("^2.7.3");
    }

    [Test]
    public async Task ExistingPeerDependencies_KeepOtherEntries()
    {
        var root = CreateTempDir();

        WriteManifest(
            root,
            """{ "name": "consumer", "peerDependencies": { "solid-js": "^1.9.0" } }"""
        );

        WritePackageJson(root, Specifier);

        var peers = ReadJson(root)["peerDependencies"] as JsonObject;

        await Assert.That(peers!["solid-js"]?.GetValue<string>()).IsEqualTo("^1.9.0");
        await Assert.That(peers[RuntimeName]?.GetValue<string>()).IsEqualTo(Specifier);
    }

    [Test]
    public async Task CrossPackageDependencies_StillMergedIntoDependencies()
    {
        var root = CreateTempDir();
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(
            root,
            srcDir,
            [],
            authoritativePackageName: "consumer",
            crossPackageDependencies: new Dictionary<string, string>
            {
                ["@acme/shared"] = "^1.2.3",
            },
            runtimePeerSpecifier: Specifier
        );

        var pkg = ReadJson(root);

        await Assert
            .That(pkg["dependencies"]?["@acme/shared"]?.GetValue<string>())
            .IsEqualTo("^1.2.3");
        await Assert.That(DeclaredVersion(pkg, "peerDependencies")).IsEqualTo(Specifier);
    }

    [Test]
    public async Task Regeneration_IsIdempotent()
    {
        var root = CreateTempDir();

        WritePackageJson(root, Specifier);

        var first = File.ReadAllText(Path.Combine(root, "package.json"));

        WritePackageJson(root, Specifier);

        await Assert.That(File.ReadAllText(Path.Combine(root, "package.json"))).IsEqualTo(first);
    }

    [Test]
    [Arguments("2.7.2", "^2.7.2")]
    [Arguments("2.7.2+4d3c5fd4a1b2", "^2.7.2")]
    [Arguments("3.0.0-preview.0.12", "*")]
    [Arguments("0.0.0", "*")]
    [Arguments("0.0.0-alpha.0", "*")]
    [Arguments("", "*")]
    [Arguments(null, "*")]
    public async Task SpecifierFor_MapsVersionToCaretOrWildcard(string? version, string expected) =>
        await Assert.That(RuntimePeerDependency.SpecifierFor(version)).IsEqualTo(expected);

    [Test]
    public async Task Transform_RecordNeedingTheRuntime_CapturesTheCurrentSpecifier()
    {
        var inputs = TransformInputs(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Acme;

            public record Money(int Amount);
            """
        );

        await Assert
            .That(inputs.RuntimePeerSpecifier)
            .IsEqualTo(RuntimePeerDependency.CurrentSpecifier);
    }

    [Test]
    public async Task Transform_PlainEnum_CapturesNoRuntimePeer()
    {
        var inputs = TransformInputs(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Acme;

            public enum Color { Red, Green }
            """
        );

        await Assert.That(inputs.RuntimePeerSpecifier).IsNull();
    }

    private static PackageManifestInputs TransformInputs(string source)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var target = new TypeScriptTarget();

        target.Transform(ir, compilation);

        return target.LastPackageManifestInputs;
    }

    private static string PeerManifest(string runtimeSpecifier) =>
        $$"""{ "name": "consumer", "peerDependencies": { "metano-runtime": "{{runtimeSpecifier}}" } }""";

    private static void WriteManifest(string root, string json) =>
        File.WriteAllText(Path.Combine(root, "package.json"), json);

    private static void WritePackageJson(
        string root,
        string? runtimePeerSpecifier,
        string packageName = "consumer"
    )
    {
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(
            root,
            srcDir,
            [],
            authoritativePackageName: packageName,
            runtimePeerSpecifier: runtimePeerSpecifier
        );
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-runtime-peer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static string? DeclaredVersion(JsonObject pkg, string section) =>
        pkg[section]?[RuntimeName]?.GetValue<string>();

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
