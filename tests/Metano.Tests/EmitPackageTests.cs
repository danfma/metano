using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;

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
    private readonly List<string> _tempDirs = new();

    [After(Test)]
    public void DeleteTempDirs()
    {
        // Centralised cleanup so a test failure mid-assertion still
        // releases the temp directory. Previously each test ended with
        // a manual Directory.Delete that would not run on an exception.
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

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
    }

    [Test]
    public async Task ExistingFileWithDivergentName_WarnsAndPreservesExisting()
    {
        // Per #136, the transpiler is a write-if-missing
        // collaborator: a hand-edited package.json#name survives
        // every regeneration. When [EmitPackage] disagrees, we
        // surface MS0007 so the consumer can re-align the two
        // sources, but we never silently flip the name back —
        // doing so would invalidate links / docs / cross-package
        // imports the consumer may have already published with the
        // existing name.
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
        // Existing name preserved.
        await Assert.That(pkg["name"]?.GetValue<string>()).IsEqualTo("old-name");
        // Writer still reported MS0007 so the divergence is visible.
        await Assert
            .That(diags.Any(d => d.Code == DiagnosticCodes.CrossPackageResolution))
            .IsTrue();
        await Assert.That(diags.Any(d => d.Severity == MetanoDiagnosticSeverity.Warning)).IsTrue();
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
    }

    [Test]
    public async Task Imports_MergedWithUserDefinedEntries()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        // Existing package.json with user-defined custom import alias
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "test-pkg",
              "imports": {
                "#custom/*": "./lib/*.ts"
              }
            }
            """
        );

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();
        // Transpiler entries added
        await Assert.That(imports!.ContainsKey("#/*")).IsTrue();
        await Assert.That(imports.ContainsKey("#")).IsTrue();
        // User-defined entry preserved
        await Assert.That(imports.ContainsKey("#custom/*")).IsTrue();
    }

    [Test]
    public async Task Exports_StaleEntriesRemovedOnRegeneration()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        // Existing package.json with stale exports from a previous run
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "test-pkg",
              "exports": {
                ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" },
                "./old-barrel": { "types": "./dist/old-barrel/index.d.ts", "import": "./dist/old-barrel/index.js" }
              }
            }
            """
        );

        // Current generation only has root barrel — old-barrel was removed
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
        // Current barrel preserved
        await Assert.That(exports!.ContainsKey(".")).IsTrue();
        // Stale entry removed
        await Assert.That(exports.ContainsKey("./old-barrel")).IsFalse();
        await Assert.That(exports!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Imports_ExistingAliasesPreservedAcrossRegenerations()
    {
        // Write-if-missing rule (per #136): both transpiler-shaped
        // aliases (`#`, `#/*`) and user-defined ones survive across
        // regenerations once they exist on disk. The transpiler
        // only seeds new keys when the field is empty or missing
        // an alias entirely. Removing the obsolete `#` when its
        // barrel disappears would clobber a consumer who left the
        // alias pointing at a hand-curated bundle output.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "test-pkg",
              "imports": {
                "#": { "types": "./dist/index.d.ts", "import": "./dist/index.js", "default": "./src/index.ts" },
                "#/*": { "types": "./dist/*.d.ts", "import": "./dist/*.js", "default": "./src/*.ts" },
                "#custom/*": "./lib/*.ts"
              }
            }
            """
        );

        var files = new[] { new TsSourceFile("item.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;

        await Assert.That(imports).IsNotNull();
        await Assert.That(imports!.ContainsKey("#")).IsTrue();
        await Assert.That(imports.ContainsKey("#/*")).IsTrue();
        await Assert.That(imports.ContainsKey("#custom/*")).IsTrue();
    }

    [Test]
    public async Task Imports_RetargetedAlias_PreservedAcrossRegenerations()
    {
        // The consumer pointed `#/*` at `./lib/*` instead of the
        // default `./dist/*` — perhaps using a custom build
        // pipeline that emits to `lib/`. The transpiler must not
        // flip the alias back to `./dist/*` on every regeneration.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "imports": {
                "#/*": {
                  "types": "./lib/*.d.ts",
                  "import": "./lib/*.js",
                  "default": "./src/*.ts"
                }
              }
            }
            """
        );

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var subpathAlias = pkg["imports"]?["#/*"] as JsonObject;

        await Assert.That(subpathAlias).IsNotNull();
        await Assert.That(subpathAlias!["types"]?.GetValue<string>()).IsEqualTo("./lib/*.d.ts");
        await Assert.That(subpathAlias["import"]?.GetValue<string>()).IsEqualTo("./lib/*.js");
    }

    [Test]
    public async Task Exports_SubdirectoryOutput_IncludesPrefix()
    {
        var tempDir = CreateTempDir();
        // Output to src/domain/ — a subdirectory of the source root
        var srcDir = Path.Combine(tempDir, "src", "domain");
        Directory.CreateDirectory(srcDir);

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("users/index.ts", [], ""),
            new TsSourceFile("users/user.ts", [], ""),
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
        // Subpaths include "domain" prefix
        await Assert.That(exports!.ContainsKey("./domain")).IsTrue();
        await Assert.That(exports.ContainsKey("./domain/users")).IsTrue();
        // Root "." must NOT exist (output is not at source root)
        await Assert.That(exports.ContainsKey(".")).IsFalse();
        // Dist paths include the prefix
        var rootEntry = exports["./domain"] as JsonObject;
        await Assert
            .That(rootEntry!["types"]?.GetValue<string>())
            .IsEqualTo("./dist/domain/index.d.ts");
    }

    [Test]
    public async Task Imports_SubdirectoryOutput_DistPathsIncludePrefix()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src", "domain");
        Directory.CreateDirectory(srcDir);

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();

        var wildcard = imports!["#/*"] as JsonObject;
        await Assert.That(wildcard).IsNotNull();
        // Dist paths include "domain/" prefix
        await Assert.That(wildcard!["types"]?.GetValue<string>()).IsEqualTo("./dist/domain/*.d.ts");
        await Assert.That(wildcard["import"]?.GetValue<string>()).IsEqualTo("./dist/domain/*.js");
        // Source paths use the full srcRelative (unchanged)
        await Assert.That(wildcard["default"]?.GetValue<string>()).IsEqualTo("./src/domain/*.ts");
    }

    [Test]
    public async Task Exports_ExplicitSrcRoot_OverridesDefault()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "lib", "models");
        Directory.CreateDirectory(srcDir);

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg",
            srcRoot: "lib"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        // srcRoot = "lib", srcRelative = "lib/models" → prefix = "models"
        await Assert.That(exports!.ContainsKey("./models")).IsTrue();
        await Assert.That(exports.ContainsKey(".")).IsFalse();
    }

    [Test]
    public async Task Exports_SrcRootDot_UsesFullSrcRelativeAsPrefix()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "domain");
        Directory.CreateDirectory(srcDir);

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        // srcRoot = "." means package root is the source root,
        // so srcRelative ("domain") becomes the full prefix.
        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg",
            srcRoot: "."
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./domain")).IsTrue();
        await Assert.That(exports.ContainsKey(".")).IsFalse();
    }

    [Test]
    public async Task Exports_SrcRootWithTrailingSlash_NormalizedCorrectly()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src", "domain");
        Directory.CreateDirectory(srcDir);

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        // Trailing slash should be stripped — "src/" behaves like "src"
        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "test-pkg",
            srcRoot: "src/"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;
        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./domain")).IsTrue();
        await Assert.That(exports.ContainsKey(".")).IsFalse();
    }

    // ── Write-if-missing rule for hand-curated fields ────────────────

    [Test]
    public async Task ExistingTypeField_PreservedAcrossRegeneration()
    {
        // A consumer who set `"type": "commonjs"` (or "module" — the
        // default — explicitly) keeps that value. The transpiler used
        // to flip the field back to "module" on every run, which
        // destroyed CJS dual-build configurations.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """{ "name": "consumer", "type": "commonjs" }"""
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")]
        );

        var pkg = ReadJson(tempDir);
        await Assert.That(pkg["type"]?.GetValue<string>()).IsEqualTo("commonjs");
    }

    [Test]
    public async Task MissingTypeField_SeededAsModule()
    {
        // First-run behavior is unchanged: when the consumer has
        // not picked a value, the transpiler seeds "module" since
        // every emitted file is ESM.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")]
        );

        var pkg = ReadJson(tempDir);
        await Assert.That(pkg["type"]?.GetValue<string>()).IsEqualTo("module");
    }

    [Test]
    public async Task ExistingSideEffectsArray_PreservedAcrossRegeneration()
    {
        // Hand-curated `sideEffects: [...]` (e.g. listing CSS imports
        // for tree-shakers) survives. The transpiler seeded `false`
        // unconditionally before, which clobbered carefully tuned
        // bundler hints.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "sideEffects": ["./styles.css", "./register.ts"]
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")]
        );

        var pkg = ReadJson(tempDir);
        var sideEffects = pkg["sideEffects"] as JsonArray;
        await Assert.That(sideEffects).IsNotNull();
        await Assert.That(sideEffects!.Count).IsEqualTo(2);
        await Assert.That(sideEffects[0]?.GetValue<string>()).IsEqualTo("./styles.css");
        await Assert.That(sideEffects[1]?.GetValue<string>()).IsEqualTo("./register.ts");
    }

    [Test]
    public async Task UserAddedExport_SurvivesRegeneration()
    {
        // The user added a "./styles.css" export that points
        // outside the dist tree. The transpiler must not strip it
        // when refreshing transpiler-managed entries.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                "./styles.css": "./assets/styles.css",
                ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" }
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;

        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./styles.css")).IsTrue();
        await Assert.That(exports.ContainsKey(".")).IsTrue();
    }

    [Test]
    public async Task UserAddedConditionalField_PreservedOnTranspilerKey()
    {
        // The consumer augmented the transpiler-managed `.` entry
        // with a `require` condition (e.g. CJS dual build). The
        // per-key deep-merge must refresh `types` and `import`
        // without erasing `require`.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                ".": {
                  "types": "./dist/index.d.ts",
                  "import": "./dist/index.js",
                  "require": "./dist/index.cjs"
                }
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var rootExport = pkg["exports"]?[".".AsSpan().ToString()] as JsonObject;

        await Assert.That(rootExport).IsNotNull();
        await Assert.That(rootExport!["types"]?.GetValue<string>()).IsEqualTo("./dist/index.d.ts");
        await Assert.That(rootExport["import"]?.GetValue<string>()).IsEqualTo("./dist/index.js");
        await Assert.That(rootExport["require"]?.GetValue<string>()).IsEqualTo("./dist/index.cjs");
    }

    [Test]
    public async Task ExecutableConsumer_PreservesExistingExports()
    {
        // Executables don't get transpiler-emitted exports, so the
        // writer must leave the consumer's hand-curated `exports`
        // object untouched. The pre-fix behavior unconditionally
        // removed the field on every run.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                "./styles.css": "./assets/styles.css"
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("program.ts", [], "")],
            authoritativePackageName: "consumer",
            isExecutable: true
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;

        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./styles.css")).IsTrue();
    }

    [Test]
    public async Task SiblingDistDirectoryExport_NotMisclassifiedAsStale()
    {
        // Path-boundary check: with `./dist` as the transpiler dist
        // prefix, a hand-curated entry pointing into a sibling
        // directory like `./dist-cjs/...` must not be misclassified
        // as transpiler-emitted via a substring match.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                "./cjs": {
                  "types": "./dist-cjs/index.d.ts",
                  "import": "./dist-cjs/index.js"
                },
                ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" }
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;

        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./cjs")).IsTrue();
        await Assert.That(exports.ContainsKey(".")).IsTrue();
    }

    [Test]
    public async Task LibraryWithoutBarrels_PrunesStaleTranspilerEntries()
    {
        // Library generation that no longer emits any barrel still
        // needs the merge pass: previously transpiler-shaped entries
        // from an earlier run point at deleted dist files and must
        // be dropped, while user-added subpaths survive.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" },
                "./styles.css": "./assets/styles.css"
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("internal.ts", [], "")],
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;

        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey(".")).IsFalse();
        await Assert.That(exports.ContainsKey("./styles.css")).IsTrue();
    }

    [Test]
    public async Task NonStringExportFields_DoNotCrashWriter()
    {
        // Defensive read: a hand-edited entry might put a JSON
        // object or array under `types` / `import` (e.g. nested
        // conditional exports). The writer must classify the entry
        // as user-curated and preserve it instead of crashing on
        // the type mismatch.
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "consumer",
              "exports": {
                "./nested": {
                  "types": { "default": "./types/nested.d.ts" },
                  "import": "./dist/nested.js"
                }
              }
            }
            """
        );

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files: [new TsSourceFile("index.ts", [], "")],
            authoritativePackageName: "consumer"
        );

        var pkg = ReadJson(tempDir);
        var exports = pkg["exports"] as JsonObject;

        await Assert.That(exports).IsNotNull();
        await Assert.That(exports!.ContainsKey("./nested")).IsTrue();
    }

    // ── Configurable isolated import alias (feature 004) ─────────────

    [Test]
    public async Task Imports_WithImportAlias_UsesAliasScopedKeysOnly()
    {
        var tempDir = CreateTempDir();
        // Generated code lands in a subfolder of an existing project's src root.
        var srcDir = Path.Combine(tempDir, "src", "abc", "contracts");
        Directory.CreateDirectory(srcDir);

        var files = new[]
        {
            new TsSourceFile("index.ts", [], ""),
            new TsSourceFile("serialization/index.ts", [], ""),
        };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "abc-contracts",
            importAlias: "contracts"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();
        // Alias-scoped keys present.
        await Assert.That(imports!.ContainsKey("#contracts/*")).IsTrue();
        await Assert.That(imports.ContainsKey("#contracts")).IsTrue();
        // Default keys are NOT emitted when an alias is set.
        await Assert.That(imports.ContainsKey("#/*")).IsFalse();
        await Assert.That(imports.ContainsKey("#")).IsFalse();
        // Targets are scoped to the output subfolder.
        var wildcard = imports["#contracts/*"] as JsonObject;
        await Assert
            .That(wildcard!["default"]?.GetValue<string>())
            .IsEqualTo("./src/abc/contracts/*.ts");
        await Assert
            .That(wildcard["types"]?.GetValue<string>())
            .IsEqualTo("./dist/abc/contracts/*.d.ts");
    }

    [Test]
    public async Task Imports_WithImportAlias_PreservesHostOwnHashAlias()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src", "abc", "contracts");
        Directory.CreateDirectory(srcDir);
        // Host project already uses `#/*` for its own src root.
        File.WriteAllText(
            Path.Combine(tempDir, "package.json"),
            """
            {
              "name": "host-app",
              "imports": {
                "#/*": { "default": "./src/*.ts" }
              }
            }
            """
        );

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir,
            files,
            authoritativePackageName: "host-app",
            importAlias: "contracts"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();
        // Host's own `#/*` is left exactly as it was.
        var hostAlias = imports!["#/*"] as JsonObject;
        await Assert.That(hostAlias!["default"]?.GetValue<string>()).IsEqualTo("./src/*.ts");
        // Metano's isolated alias is added alongside, without touching `#`.
        await Assert.That(imports.ContainsKey("#contracts/*")).IsTrue();
        await Assert.That(imports.ContainsKey("#contracts")).IsTrue();
        await Assert.That(imports.ContainsKey("#")).IsFalse();
    }

    [Test]
    public async Task Imports_NestedOutputWithTrailingSlash_NoDoubleSlash()
    {
        var tempDir = CreateTempDir();
        var srcDir = Path.Combine(tempDir, "src", "domain");
        Directory.CreateDirectory(srcDir);

        var files = new[] { new TsSourceFile("index.ts", [], "") };

        // Output dir carries a trailing separator — the path-joining must still
        // produce single-slash segments (no `.../domain//index.d.ts`).
        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            srcDir + Path.DirectorySeparatorChar,
            files,
            authoritativePackageName: "test-pkg"
        );

        var pkg = ReadJson(tempDir);
        var imports = pkg["imports"] as JsonObject;
        await Assert.That(imports).IsNotNull();
        var wildcard = imports!["#/*"] as JsonObject;
        await Assert.That(wildcard!["types"]?.GetValue<string>()).DoesNotContain("//");
        await Assert.That(wildcard["import"]?.GetValue<string>()).DoesNotContain("//");
        await Assert.That(wildcard["default"]?.GetValue<string>()).DoesNotContain("//");
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metasharp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
