using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// #424: the export condition that resolves a generated package to its <c>.ts</c> sources
/// is configurable (<c>--source-condition</c>, MSBuild <c>MetanoSourceCondition</c>), so a
/// consumer can enable it for that package alone. The default stays <c>source</c>.
/// </summary>
public class SourceConditionTests
{
    private const string CustomName = "metano-source";

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    // ── SourceCondition value object ───────────────────────────────────────

    [Test]
    public async Task DefaultIsSource()
    {
        await Assert.That(default(SourceCondition)).IsEqualTo(SourceCondition.Default);
        await Assert.That(SourceCondition.Default.Name).IsEqualTo("source");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task BlankValue_MeansDefault(string? raw)
    {
        var parsed = SourceCondition.TryFrom(raw, out var condition, out var diagnostic);

        await Assert.That(parsed).IsTrue();
        await Assert.That(condition).IsEqualTo(SourceCondition.Default);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    [Arguments("metano-source")]
    [Arguments("source")]
    [Arguments("my_pkg.source:ts")]
    [Arguments("Dev2")]
    [Arguments("v1")]
    public async Task ValidName_IsAccepted(string raw)
    {
        var parsed = SourceCondition.TryFrom(raw, out var condition, out var diagnostic);

        await Assert.That(parsed).IsTrue();
        await Assert.That(condition.Name).IsEqualTo(raw);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    [Arguments("my source", "only letters, digits")]
    [Arguments("a/b", "only letters, digits")]
    [Arguments("@scope", "only letters, digits")]
    [Arguments("123", "made only of digits")]
    [Arguments("import", "built-in Node resolution condition")]
    [Arguments("require", "built-in Node resolution condition")]
    [Arguments("default", "built-in Node resolution condition")]
    [Arguments("types", "built-in Node resolution condition")]
    [Arguments("node", "built-in Node resolution condition")]
    [Arguments("browser", "built-in Node resolution condition")]
    [Arguments("module", "bundlers enable by default")]
    [Arguments("development", "bundlers enable by default")]
    [Arguments("production", "bundlers enable by default")]
    public async Task InvalidName_IsRejectedWithMs0059(string raw, string expectedProblem)
    {
        var parsed = SourceCondition.TryFrom(raw, out var condition, out var diagnostic);

        await Assert.That(parsed).IsFalse();
        await Assert.That(condition).IsEqualTo(SourceCondition.Default);
        await Assert.That(diagnostic!.Code).IsEqualTo("MS0059");
        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(diagnostic.Message).Contains($"Invalid source condition '{raw}'");
        await Assert.That(diagnostic.Message).Contains(expectedProblem);
    }

    // ── PackageJsonWriter ──────────────────────────────────────────────────

    [Test]
    public async Task Default_WritesSourceCondition_ByteIdenticalToUnsetCondition()
    {
        var unset = WriteFresh(default);
        var explicitDefault = WriteFresh(ConditionOf("source"));
        var exportsOrder = OrderOf(ExportsOf(unset), ".");
        var importsOrder = OrderOf(ImportsOf(unset), "#/*");

        await Assert.That(explicitDefault).IsEqualTo(unset);
        await Assert.That(exportsOrder).IsEqualTo("source,types,import");
        await Assert.That(importsOrder).IsEqualTo("source,types,import,default");
    }

    [Test]
    public async Task CustomName_ReplacesSourceInExportsAndImports()
    {
        var json = WriteFresh(ConditionOf(CustomName));
        var exports = ExportsOf(json);
        var imports = ImportsOf(json);

        await Assert.That(OrderOf(exports, ".")).IsEqualTo($"{CustomName},types,import");

        await Assert
            .That(exports["."]![CustomName]!.GetValue<string>())
            .IsEqualTo("./src/index.ts");

        await Assert
            .That(exports["./users"]![CustomName]!.GetValue<string>())
            .IsEqualTo("./src/users/index.ts");

        await Assert.That(OrderOf(imports, "#/*")).IsEqualTo($"{CustomName},types,import,default");
        await Assert.That(imports["#/*"]![CustomName]!.GetValue<string>()).IsEqualTo("./src/*.ts");

        await Assert
            .That(imports["#"]![CustomName]!.GetValue<string>())
            .IsEqualTo("./src/index.ts");

        await Assert.That(exports["."]!.AsObject().ContainsKey("source")).IsFalse();
        await Assert.That(imports["#/*"]!.AsObject().ContainsKey("source")).IsFalse();
    }

    [Test]
    public async Task Default_KeepsUserConditionsBeforeTypesPointingAtTheSources()
    {
        var dir = CreateTempDir();

        Write(dir, default);
        AddUserConditionsBeforeTypes(dir);

        Write(dir, default);

        var json = ReadJson(dir);

        await Assert.That(OrderOf(ExportsOf(json), ".")).IsEqualTo("bun,source,types,import");

        await Assert
            .That(OrderOf(ImportsOf(json), "#/*"))
            .IsEqualTo("development,source,types,import,default");
    }

    [Test]
    public async Task Rename_FromDefault_RemovesSourceAndKeepsUserConditions()
    {
        var dir = CreateTempDir();

        Write(dir, default);
        AddUserConditionsBeforeTypes(dir);
        AddRequireAfterTypes(dir);

        Write(dir, ConditionOf(CustomName));

        var json = ReadJson(dir);
        var exports = ExportsOf(json);
        var imports = ImportsOf(json);

        await Assert
            .That(OrderOf(exports, "."))
            .IsEqualTo($"{CustomName},bun,types,import,require");

        await Assert.That(OrderOf(exports, "./users")).IsEqualTo($"{CustomName},types,import");

        await Assert
            .That(OrderOf(imports, "#/*"))
            .IsEqualTo($"{CustomName},development,types,import,default");

        await Assert.That(OrderOf(imports, "#")).IsEqualTo($"{CustomName},types,import,default");
        await Assert.That(exports["."]!["bun"]!.GetValue<string>()).IsEqualTo("./src/index.ts");
        await Assert.That(exports["."]!["require"]!.GetValue<string>()).IsEqualTo("./legacy.cjs");

        await Assert
            .That(imports["#/*"]!["development"]!.GetValue<string>())
            .IsEqualTo("./src/*.ts");
    }

    [Test]
    public async Task Rename_BetweenCustomNames_LeavesTheOldCustomKeyForTheUser()
    {
        var dir = CreateTempDir();

        Write(dir, ConditionOf("old-source"));
        Write(dir, ConditionOf(CustomName));

        var json = ReadJson(dir);

        await Assert
            .That(OrderOf(ExportsOf(json), "."))
            .IsEqualTo($"{CustomName},old-source,types,import");

        await Assert
            .That(OrderOf(ImportsOf(json), "#/*"))
            .IsEqualTo($"{CustomName},old-source,types,import,default");
    }

    [Test]
    public async Task Rename_KeepsARetargetedUserSourceCondition()
    {
        var dir = CreateTempDir();

        Write(dir, default);

        var json = ReadJson(dir);

        ExportsOf(json)["."]!.AsObject()["source"] = "./custom/index.ts";
        File.WriteAllText(Path.Combine(dir, "package.json"), json.ToJsonString());

        Write(dir, ConditionOf(CustomName));

        var root = ExportsOf(ReadJson(dir))["."];

        await Assert.That(ConditionOrder(root)).IsEqualTo($"{CustomName},source,types,import");
        await Assert.That(root!["source"]!.GetValue<string>()).IsEqualTo("./custom/index.ts");
    }

    [Test]
    public async Task Rename_NeverTouchesAConditionAfterTypes()
    {
        var dir = CreateTempDir();

        Write(dir, default);

        var json = ReadJson(dir);

        ExportsOf(json)["."]!.AsObject()["development"] = "./src/index.ts";
        File.WriteAllText(Path.Combine(dir, "package.json"), json.ToJsonString());

        Write(dir, ConditionOf(CustomName));

        var order = OrderOf(ExportsOf(ReadJson(dir)), ".");

        await Assert.That(order).IsEqualTo($"{CustomName},types,import,development");
    }

    [Test]
    public async Task Reconciler_PassesTheConditionToTheWriter()
    {
        var dir = CreateTempDir();
        var outputDir = Path.Combine(dir, "src");
        var resolution = PackageRootResolver.Resolve(outputDir, "App.csproj", dir);

        var inputs = new PackageManifestInputs(
            SourceFiles: Barrels(),
            PackageName: null,
            SubpathRoot: PackageSubpathRoot.None,
            CrossPackageDependencies: new Dictionary<string, string>(),
            IsExecutable: false
        );

        PackageJsonReconciler.Reconcile(
            inputs,
            resolution,
            outputDir,
            dist: "./dist",
            srcRoot: null,
            importAlias: null,
            sourceCondition: ConditionOf(CustomName)
        );

        var order = OrderOf(ExportsOf(ReadJson(dir)), ".");

        await Assert.That(order).IsEqualTo($"{CustomName},types,import");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string WriteFresh(SourceCondition condition)
    {
        var dir = CreateTempDir();

        Write(dir, condition);

        return File.ReadAllText(Path.Combine(dir, "package.json"));
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-source-condition-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static void Write(string dir, SourceCondition condition) =>
        PackageJsonWriter.UpdateOrCreate(
            dir,
            Path.Combine(dir, "src"),
            Barrels(),
            authoritativePackageName: "pkg",
            srcRoot: "src",
            sourceCondition: condition
        );

    private static void AddUserConditionsBeforeTypes(string dir)
    {
        var json = ReadJson(dir);

        ExportsOf(json)["."]!.AsObject().Insert(0, "bun", "./src/index.ts");
        ImportsOf(json)["#/*"]!.AsObject().Insert(0, "development", "./src/*.ts");
        File.WriteAllText(Path.Combine(dir, "package.json"), json.ToJsonString());
    }

    private static void AddRequireAfterTypes(string dir)
    {
        var json = ReadJson(dir);

        ExportsOf(json)["."]!.AsObject()["require"] = "./legacy.cjs";
        File.WriteAllText(Path.Combine(dir, "package.json"), json.ToJsonString());
    }

    private static SourceCondition ConditionOf(string name)
    {
        SourceCondition.TryFrom(name, out var condition, out _);

        return condition;
    }

    private static TsSourceFile[] Barrels() =>
        [new TsSourceFile("index.ts", [], ""), new TsSourceFile("users/index.ts", [], "")];

    private static string OrderOf(JsonObject section, string key) => ConditionOrder(section[key]);

    private static string ConditionOrder(JsonNode? entry) =>
        string.Join(",", entry!.AsObject().Select(condition => condition.Key));

    private static JsonObject ExportsOf(string json) =>
        (JsonNode.Parse(json)!["exports"] as JsonObject)!;

    private static JsonObject ExportsOf(JsonObject json) => (json["exports"] as JsonObject)!;

    private static JsonObject ImportsOf(string json) =>
        (JsonNode.Parse(json)!["imports"] as JsonObject)!;

    private static JsonObject ImportsOf(JsonObject json) => (json["imports"] as JsonObject)!;

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
