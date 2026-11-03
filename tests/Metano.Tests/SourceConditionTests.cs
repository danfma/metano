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
    [Arguments("import", "built-in resolution condition")]
    [Arguments("require", "built-in resolution condition")]
    [Arguments("default", "built-in resolution condition")]
    [Arguments("types", "built-in resolution condition")]
    [Arguments("node", "built-in resolution condition")]
    [Arguments("browser", "built-in resolution condition")]
    public async Task InvalidName_IsRejectedWithMs0059(string raw, string expectedProblem)
    {
        var parsed = SourceCondition.TryFrom(raw, out var condition, out var diagnostic);

        await Assert.That(parsed).IsFalse();
        await Assert.That(condition).IsEqualTo(SourceCondition.Default);
        await Assert.That(diagnostic!.Code).IsEqualTo(DiagnosticCodes.InvalidSourceCondition);
        await Assert.That(diagnostic.Code).IsEqualTo("MS0059");
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

        await Assert.That(explicitDefault).IsEqualTo(unset);
        await Assert.That(ConditionOrder(ExportsOf(unset)["."])).IsEqualTo("source,types,import");
        await Assert
            .That(ConditionOrder(ImportsOf(unset)["#/*"]))
            .IsEqualTo("source,types,import,default");
    }

    [Test]
    public async Task CustomName_ReplacesSourceInExportsAndImports()
    {
        var json = WriteFresh(ConditionOf(CustomName));

        var root = ExportsOf(json)["."];
        var users = ExportsOf(json)["./users"];
        var wildcard = ImportsOf(json)["#/*"];
        var rootAlias = ImportsOf(json)["#"];

        await Assert.That(ConditionOrder(root)).IsEqualTo($"{CustomName},types,import");
        await Assert.That(root![CustomName]!.GetValue<string>()).IsEqualTo("./src/index.ts");
        await Assert.That(users![CustomName]!.GetValue<string>()).IsEqualTo("./src/users/index.ts");
        await Assert.That(ConditionOrder(wildcard)).IsEqualTo($"{CustomName},types,import,default");
        await Assert.That(wildcard![CustomName]!.GetValue<string>()).IsEqualTo("./src/*.ts");
        await Assert.That(rootAlias![CustomName]!.GetValue<string>()).IsEqualTo("./src/index.ts");
        await Assert.That(json).DoesNotContain("\"source\"");
    }

    [Test]
    public async Task Rename_FromDefault_ReplacesManagedEntryAndKeepsUserConditions()
    {
        var dir = CreateTempDir();

        Write(dir, default);
        AddUserConditions(dir);

        Write(dir, ConditionOf(CustomName));

        var json = ReadJson(dir);

        await Assert
            .That(ConditionOrder(ExportsOf(json)["."]))
            .IsEqualTo($"{CustomName},types,import,require");
        await Assert
            .That(ConditionOrder(ExportsOf(json)["./users"]))
            .IsEqualTo($"{CustomName},types,import");
        await Assert
            .That(ConditionOrder(ImportsOf(json)["#/*"]))
            .IsEqualTo($"{CustomName},types,import,bun,default");
        await Assert
            .That(ConditionOrder(ImportsOf(json)["#"]))
            .IsEqualTo($"{CustomName},types,import,default");
        await Assert
            .That(ExportsOf(json)["."]!["require"]!.GetValue<string>())
            .IsEqualTo("./legacy.cjs");
        await Assert
            .That(ImportsOf(json)["#/*"]!["bun"]!.GetValue<string>())
            .IsEqualTo("./bun/*.ts");
    }

    [Test]
    public async Task Rename_BetweenCustomNames_LeavesOnlyTheNewOne()
    {
        var dir = CreateTempDir();

        Write(dir, ConditionOf("old-source"));
        Write(dir, ConditionOf(CustomName));

        var json = ReadJson(dir);

        await Assert
            .That(ConditionOrder(ExportsOf(json)["."]))
            .IsEqualTo($"{CustomName},types,import");
        await Assert
            .That(ConditionOrder(ImportsOf(json)["#/*"]))
            .IsEqualTo($"{CustomName},types,import,default");
    }

    [Test]
    public async Task Rename_BackToDefault_DropsTheFormerCustomName()
    {
        var dir = CreateTempDir();

        Write(dir, ConditionOf(CustomName));
        Write(dir, default);

        var json = ReadJson(dir);

        await Assert.That(ConditionOrder(ExportsOf(json)["."])).IsEqualTo("source,types,import");
        await Assert
            .That(ConditionOrder(ImportsOf(json)["#/*"]))
            .IsEqualTo("source,types,import,default");
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

        var renamed = ReadJson(dir);
        var root = ExportsOf(renamed)["."];

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

        await Assert
            .That(ConditionOrder(ExportsOf(ReadJson(dir))["."]))
            .IsEqualTo($"{CustomName},types,import,development");
    }

    [Test]
    public async Task Reconciler_PassesTheConditionToTheWriter()
    {
        var dir = CreateTempDir();
        var resolution = PackageRootResolver.Resolve(Path.Combine(dir, "src"), "App.csproj", dir);
        var inputs = new PackageManifestInputs(
            Barrels(),
            null,
            PackageSubpathRoot.None,
            new Dictionary<string, string>(),
            false
        );

        PackageJsonReconciler.Reconcile(
            inputs,
            resolution,
            Path.Combine(dir, "src"),
            "./dist",
            null,
            null,
            ConditionOf(CustomName)
        );

        await Assert
            .That(ConditionOrder(ExportsOf(ReadJson(dir))["."]))
            .IsEqualTo($"{CustomName},types,import");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string WriteFresh(SourceCondition condition)
    {
        var dir = CreateTempDir();

        Write(dir, condition);

        return File.ReadAllText(Path.Combine(dir, "package.json"));
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

    private static void AddUserConditions(string dir)
    {
        var json = ReadJson(dir);

        ExportsOf(json)["."]!.AsObject()["require"] = "./legacy.cjs";
        ImportsOf(json)["#/*"]!.AsObject().Insert(3, "bun", "./bun/*.ts");
        File.WriteAllText(Path.Combine(dir, "package.json"), json.ToJsonString());
    }

    private static SourceCondition ConditionOf(string name)
    {
        SourceCondition.TryFrom(name, out var condition, out _);

        return condition;
    }

    private static TsSourceFile[] Barrels() =>
        [new TsSourceFile("index.ts", [], ""), new TsSourceFile("users/index.ts", [], "")];

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

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-source-condition-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }
}
