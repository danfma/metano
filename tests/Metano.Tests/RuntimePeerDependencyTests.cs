using System.Text.Json.Nodes;
using Metano.Compiler;

namespace Metano.Tests;

/// <summary>
/// The generated package.json declares <c>metano-runtime</c> as a peer dependency when an
/// emitted file imports it, without ever overriding what the consumer already declared (#411).
/// </summary>
public class RuntimePeerDependencyTests
{
    private const string Specifier = "^9.8.7";

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

        Write(root, importsRuntime: true);

        var pkg = ReadJson(root);
        await Assert
            .That(pkg["peerDependencies"]?["metano-runtime"]?.GetValue<string>())
            .IsEqualTo(Specifier);
        await Assert.That(pkg["dependencies"]).IsNull();
    }

    [Test]
    public async Task NoRuntimeImport_AddsNothing()
    {
        var root = CreateTempDir();

        Write(root, importsRuntime: false);

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
    [Arguments("peerDependencies", ">=2.0.0")]
    public async Task AlreadyDeclared_IsLeftUntouchedWithoutDuplicate(
        string section,
        string declared
    )
    {
        var root = CreateTempDir();
        File.WriteAllText(
            Path.Combine(root, "package.json"),
            $$"""{ "name": "consumer", "{{section}}": { "metano-runtime": "{{declared}}" } }"""
        );

        Write(root, importsRuntime: true);

        var pkg = ReadJson(root);
        await Assert.That(pkg[section]?["metano-runtime"]?.GetValue<string>()).IsEqualTo(declared);

        var sectionsDeclaring = new[]
        {
            "dependencies",
            "devDependencies",
            "peerDependencies",
        }.Count(name => (pkg[name] as JsonObject)?.ContainsKey("metano-runtime") == true);
        await Assert.That(sectionsDeclaring).IsEqualTo(1);
    }

    [Test]
    public async Task ExistingPeerDependencies_KeepOtherEntries()
    {
        var root = CreateTempDir();
        File.WriteAllText(
            Path.Combine(root, "package.json"),
            """{ "name": "consumer", "peerDependencies": { "solid-js": "^1.9.0" } }"""
        );

        Write(root, importsRuntime: true);

        var peers = ReadJson(root)["peerDependencies"] as JsonObject;
        await Assert.That(peers!["solid-js"]?.GetValue<string>()).IsEqualTo("^1.9.0");
        await Assert.That(peers["metano-runtime"]?.GetValue<string>()).IsEqualTo(Specifier);
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
            runtimeVersionSpecifier: Specifier,
            importsRuntime: true
        );

        var pkg = ReadJson(root);
        await Assert
            .That(pkg["dependencies"]?["@acme/shared"]?.GetValue<string>())
            .IsEqualTo("^1.2.3");
        await Assert
            .That(pkg["peerDependencies"]?["metano-runtime"]?.GetValue<string>())
            .IsEqualTo(Specifier);
    }

    [Test]
    public async Task Regeneration_IsIdempotent()
    {
        var root = CreateTempDir();

        Write(root, importsRuntime: true);
        var first = File.ReadAllText(Path.Combine(root, "package.json"));
        Write(root, importsRuntime: true);

        await Assert.That(File.ReadAllText(Path.Combine(root, "package.json"))).IsEqualTo(first);
    }

    [Test]
    public async Task DefaultSpecifier_IsDerivedFromTheCompilerVersion()
    {
        var root = CreateTempDir();
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(root, srcDir, [], importsRuntime: true);

        await Assert
            .That(ReadJson(root)["peerDependencies"]?["metano-runtime"]?.GetValue<string>())
            .IsEqualTo(RuntimePeerDependency.CurrentSpecifier);
    }

    [Test]
    [Arguments("2.7.2", "^2.7.2")]
    [Arguments("2.7.2+4d3c5fd4a1b2", "^2.7.2")]
    [Arguments("3.0.0-preview.0.12", "*")]
    [Arguments("0.0.0", "*")]
    [Arguments("0.0.0-alpha.0", "*")]
    [Arguments("", "*")]
    [Arguments(null, "*")]
    public async Task SpecifierFor_MapsReleaseVersions(string? version, string expected) =>
        await Assert.That(RuntimePeerDependency.SpecifierFor(version)).IsEqualTo(expected);

    private static void Write(string root, bool importsRuntime)
    {
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(
            root,
            srcDir,
            [],
            authoritativePackageName: "consumer",
            runtimeVersionSpecifier: Specifier,
            importsRuntime: importsRuntime
        );
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-runtime-peer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
