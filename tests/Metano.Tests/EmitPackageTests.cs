using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;
using Metano.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// Tests for the <c>[assembly: EmitPackage(name)]</c> integration with
/// <see cref="PackageJsonWriter"/>. The attribute is the authoritative source for the
/// generated package.json's <c>name</c> field; divergence with an existing file emits
/// MS0007 (warning) and the attribute value still wins because cross-package import
/// resolution depends on it.
/// </summary>
public class EmitPackageTests
{
    [Test]
    public async Task NoExistingFile_AuthoritativeNameWritten()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        var diags = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            authoritativePackageName: "@scope/cool-pkg"
        );

        var pkg = ReadJson(tempDir);
        await Assert.That(pkg["name"]?.GetValue<string>()).IsEqualTo("@scope/cool-pkg");
        await Assert.That(diags.Count).IsEqualTo(0);

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task ImportsIncludeRootAliasForPackageBarrel()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("domain/index.ts", [], ""),
            new TsSourceFile("domain/item.ts", [], ""),
        };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "sample-todo"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();
        await Assert
            .That((imports!["#"] as JsonObject)!["default"]?.GetValue<string>())
            .IsEqualTo("./src/index.ts");
        await Assert
            .That((imports["#/*"] as JsonObject)!["default"]?.GetValue<string>())
            .IsEqualTo("./src/*.ts");

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task ExistingFileWithMatchingName_NoWarning()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """{ "name": "sample-todo", "private": true }"""
        );

        var diags = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            authoritativePackageName: "sample-todo"
        );

        var pkg = ReadJson(tempDir);
        await Assert.That(pkg["name"]?.GetValue<string>()).IsEqualTo("sample-todo");
        await Assert.That(diags.Count).IsEqualTo(0);

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task ExistingFileWithDivergentName_WarnsAndOverwrites()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """{ "name": "old-name", "private": true }"""
        );

        var diags = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            authoritativePackageName: "new-name"
        );

        var pkg = ReadJson(tempDir);
        // Authoritative wins.
        await Assert.That(pkg["name"]?.GetValue<string>()).IsEqualTo("new-name");
        // And the writer reported MS0007.
        await Assert
            .That(diags.Any(d => d.Code == DiagnosticCodes.CrossPackageResolution))
            .IsTrue();
        await Assert.That(diags.Any(d => d.Severity == MetanoDiagnosticSeverity.Warning)).IsTrue();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task CrossPackageDependencies_MergedIntoExisting()
    {
        // The writer must add the compiler-tracked entries WITHOUT clobbering any
        // hand-written `dependencies` the user already had.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            { "name": "consumer", "private": true, "dependencies": { "react": "^18.0.0" } }
            """
        );

        var deps = new Dictionary<string, string>
        {
            ["sample-todo"] = "workspace:*",
            ["@scope/lib"] = "^1.2.3",
        };
        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            crossPackageDependencies: deps
        );

        var pkg = ReadJson(tempDir);
        var depsObj = pkg["dependencies"] as JsonObject;
        await Assert.That(depsObj).IsNotNull();
        // Hand-written entry preserved.
        await Assert.That(depsObj!["react"]?.GetValue<string>()).IsEqualTo("^18.0.0");
        // Compiler-tracked entries added.
        await Assert.That(depsObj["sample-todo"]?.GetValue<string>()).IsEqualTo("workspace:*");
        await Assert.That(depsObj["@scope/lib"]?.GetValue<string>()).IsEqualTo("^1.2.3");

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task CrossPackageDependencies_OverwriteSamePackageVersion()
    {
        // If the user previously had `sample-todo` pinned to a stale version, the new
        // compiler-tracked version overrides — this keeps the version source of truth
        // on the C# side rather than risking drift.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            { "name": "consumer", "dependencies": { "sample-todo": "^0.0.1" } }
            """
        );

        var deps = new Dictionary<string, string> { ["sample-todo"] = "^1.5.0" };
        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            crossPackageDependencies: deps
        );

        var pkg = ReadJson(tempDir);
        await Assert
            .That((pkg["dependencies"] as JsonObject)!["sample-todo"]?.GetValue<string>())
            .IsEqualTo("^1.5.0");

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task NoAuthoritativeName_PreservesExisting()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """{ "name": "hand-written", "private": true }"""
        );

        var diags = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [],
            authoritativePackageName: null
        );

        var pkg = ReadJson(tempDir);
        await Assert.That(pkg["name"]?.GetValue<string>()).IsEqualTo("hand-written");
        await Assert.That(diags.Count).IsEqualTo(0);

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task Exports_OnlyIncludeBarrelFiles()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("domain/index.ts", [], ""),
            new TsSourceFile("domain/item.ts", [], ""),
            new TsSourceFile("domain/category.ts", [], ""),
        };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        // Only barrels: "." and "./domain"
        await Assert.That(exports!.Count).IsEqualTo(2);
        await Assert.That(exports.ContainsKey(".")).IsTrue();
        await Assert.That(exports.ContainsKey("./domain")).IsTrue();
        // Individual files must NOT appear
        await Assert.That(exports.ContainsKey("./domain/item")).IsFalse();
        await Assert.That(exports.ContainsKey("./domain/category")).IsFalse();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task Exports_FlatPackageOnlyExportsRoot()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("todo-item.ts", [], ""),
            new TsSourceFile("todo-list.ts", [], ""),
        };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "sample-todo"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.Count).IsEqualTo(1);
        await Assert.That(exports.ContainsKey(".")).IsTrue();
        await Assert.That(exports.ContainsKey("./todo-item")).IsFalse();
        await Assert.That(exports.ContainsKey("./todo-list")).IsFalse();

        Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public async Task Exports_RegeneratedOnSubsequentRuns()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        // First run: stale file-based exports (simulating old behavior)
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "test-pkg",
              "exports": {
                ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" },
                "./item": { "types": "./dist/item.d.ts", "import": "./dist/item.js" }
              }
            }
            """
        );

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("item.ts", [], ""),
        };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        // Stale "./item" entry must be gone — only barrel "." remains
        await Assert.That(exports!.Count).IsEqualTo(1);
        await Assert.That(exports.ContainsKey(".")).IsTrue();
        await Assert.That(exports.ContainsKey("./item")).IsFalse();

        Directory.Delete(tempDir, recursive: true);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metasharp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
