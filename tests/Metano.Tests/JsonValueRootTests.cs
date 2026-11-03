using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Non-object <c>[JsonSerializable]</c> roots (collections, dictionaries,
/// sets, primitives, enums, branded and nullable types), the FR-031 member
/// naming, and the <c>MS0035</c> / <c>MS0036</c> diagnostics of the
/// generated <c>SerializerContext</c> (spec 009 User Story 2, #270).
/// </summary>
public class JsonValueRootTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private const string GoldenDeclarations = """
        [Transpile]
        public sealed record EpisodeSummary(string Title, int Number);

        [Transpile]
        public sealed record ShotTiming(double Start, double End);

        [Transpile]
        public sealed record Leaf(string Name);

        [Transpile, StringEnum]
        public enum Mood { Calm, Loud }

        [Transpile]
        public enum Level { Low, High }

        [Transpile, Branded]
        public readonly record struct EpisodeId(string Value);

        [Transpile]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        [JsonSerializable(typeof(EpisodeSummary))]
        [JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]
        [JsonSerializable(typeof(EpisodeSummary[]))]
        [JsonSerializable(typeof(ShotTiming[]))]
        [JsonSerializable(typeof(Dictionary<string, EpisodeSummary>))]
        [JsonSerializable(typeof(HashSet<string>))]
        [JsonSerializable(typeof(IEnumerable<Leaf>))]
        [JsonSerializable(typeof(int?))]
        [JsonSerializable(typeof(int))]
        [JsonSerializable(typeof(string))]
        [JsonSerializable(typeof(Guid))]
        [JsonSerializable(typeof(DateTimeOffset))]
        [JsonSerializable(typeof(decimal))]
        [JsonSerializable(typeof(Mood))]
        [JsonSerializable(typeof(Level))]
        [JsonSerializable(typeof(EpisodeId))]
        [JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]
        """;

    private const string CollidingNestedTypes = """
        [Transpile]
        public static class Anchor
        {
            [Transpile]
            public sealed record Image(string Url);

            [Transpile]
            public sealed record Overlay(Image Picture);
        }

        [Transpile]
        public static class Clip
        {
            [Transpile]
            public sealed record Image(double Seconds);
        }
        """;

    private const string PackageLibrary = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("shared")]

        namespace Shared;

        public sealed record Tag(string Name);
        """;

    [Test]
    public async Task ValueRoots_MatchGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);
        var expected = TranspileHelper.ReadExpected("json-context-value-roots.ts");

        await Assert.That(ts).IsEqualTo(expected);
    }

    [Test]
    public async Task ValueRoots_WithJsonSourceGenerator_MatchGolden()
    {
        // The STJ generator declares a JsonTypeInfo<T> member per root; the
        // context's imports (HashSet, UUID, Temporal) must not depend on it.
        var source = WrapInNamespace(
            GoldenDeclarations,
            "public partial class JsonContext : JsonSerializerContext;"
        );

        var (files, _) = TranspileHelper.TranspileWithJsonSourceGenerator(source);
        var expected = TranspileHelper.ReadExpected("json-context-value-roots.ts");

        await Assert.That(files[ContextPath]).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadOnlyListRoot_EmitsElementSpecWithoutListingIt()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record EpisodeSummary(string Title);

            [Transpile]
            [JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]
            """
        );

        await Assert
            .That(ts)
            .Contains("get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[] | null>");

        await Assert.That(ts).Contains("spec: () => this.episodeSummary");
        await Assert.That(ts).Contains("get episodeSummary(): TypeSpec<EpisodeSummary>");
    }

    [Test]
    public async Task ArrayRoot_EmitsArrayValueSpecAndElementObjectSpec()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record ShotTiming(double Start, double End);

            [Transpile]
            [JsonSerializable(typeof(ShotTiming[]))]
            """
        );

        await Assert.That(ts).Contains("get shotTimingArray(): ValueSpec<ShotTiming[] | null>");
        await Assert.That(ts).Contains("this.createValueSpec({");
        await Assert.That(ts).Contains("spec: () => this.shotTiming");
        await Assert.That(ts).Contains("get shotTiming(): TypeSpec<ShotTiming>");
        await Assert.That(ts).Contains("new ShotTiming(p.start as number, p.end as number)");
    }

    [Test]
    public async Task ValueRootNullability_FollowsTheCSharpType()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).Contains("get int32(): ValueSpec<number>");
        await Assert.That(ts).Contains("get guid(): ValueSpec<UUID>");
        await Assert.That(ts).Contains("get episodeId(): ValueSpec<EpisodeId>");
        await Assert.That(ts).Contains("get nullableInt32(): ValueSpec<number | null>");
        await Assert.That(ts).Contains("get string(): ValueSpec<string | null>");
    }

    [Test]
    public async Task TypeInfoPropertyName_RenamesTheGetter()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]
            """
        );

        await Assert.That(ts).Contains("get names(): ValueSpec<string[] | null>");
        await Assert.That(ts).DoesNotContain("listString");
    }

    [Test]
    public async Task TypeInfoPropertyName_IsUsedByRefsAndTypeArgumentNames()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Tree(Leaf Top);

            [Transpile]
            [JsonSerializable(typeof(Tree))]
            [JsonSerializable(typeof(List<Leaf>))]
            [JsonSerializable(typeof(Leaf), TypeInfoPropertyName = "LeafNode")]
            """
        );

        await Assert.That(ts).Contains("get leafNode(): TypeSpec<Leaf>");
        await Assert.That(ts).DoesNotContain("get leaf()");
        await Assert.That(ts).Contains("spec: () => this.leafNode");
        await Assert.That(ts).Contains("get listLeafNode(): ValueSpec<Leaf[] | null>");
    }

    [Test]
    public async Task GenericRootsWithSameOuterType_AreBothEmitted()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonSerializable(typeof(List<string>))]
            [JsonSerializable(typeof(List<List<int>>))]
            """
        );

        await Assert.That(ts).Contains("get listString(): ValueSpec<string[] | null>");
        await Assert.That(ts).Contains("get listListInt32(): ValueSpec<number[][] | null>");
    }

    [Test]
    public async Task NameOverride_IsUsedInsideGenericArgumentNames()
    {
        var ts = TranspileContext(
            """
            [Transpile, Name("User")]
            public sealed record UserDto(string Login);

            [Transpile]
            [JsonSerializable(typeof(List<UserDto>))]
            """
        );

        await Assert.That(ts).Contains("get listUser(): ValueSpec<User[] | null>");
        await Assert.That(ts).Contains("spec: () => this.user");
        await Assert.That(ts).Contains("get user(): TypeSpec<User>");
    }

    [Test]
    public async Task ReservedWordTypeName_KeepsTheBareGetterName()
    {
        // Getters sit in property position, so a JS reserved word needs no
        // escape: a type named Delete gets `delete` (it was `delete_` before
        // #270; documented in docs/serialization.md).
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Delete(string Id);

            [Transpile]
            public sealed record Default(string Id);

            [Transpile]
            [JsonSerializable(typeof(Delete))]
            [JsonSerializable(typeof(Default))]
            """
        );

        await Assert.That(ts).Contains("get delete(): TypeSpec<Delete>");
        await Assert.That(ts).Contains("private _delete?: TypeSpec<Delete>;");
        await Assert.That(ts).Contains("get default(): TypeSpec<Default>");
        await Assert.That(ts).Contains("private _default?: TypeSpec<Default>;");
        await Assert.That(ts).DoesNotContain("delete_");
    }

    [Test]
    public async Task NestedValueRootElements_AreDiscovered()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            [JsonSerializable(typeof(Dictionary<string, List<Leaf>>))]
            """
        );

        await Assert
            .That(ts)
            .Contains("get dictionaryStringListLeaf(): ValueSpec<Map<string, Leaf[]> | null>");

        await Assert.That(ts).Contains("get leaf(): TypeSpec<Leaf>");
    }

    [Test]
    public async Task DuplicateRoot_IsEmittedOnce()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonSerializable(typeof(List<string>))]
            [JsonSerializable(typeof(List<string>))]
            """
        );

        await Assert.That(CountOf(ts, "get listString()")).IsEqualTo(1);
    }

    [Test]
    public async Task ImmutableDictionaryRoot_IsAMap()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonSerializable(typeof(System.Collections.Immutable.ImmutableDictionary<string, int>))]
            """
        );

        await Assert
            .That(ts)
            .Contains(
                "get immutableDictionaryStringInt32(): ValueSpec<Map<string, number> | null>"
            );

        await Assert.That(ts).Contains("kind: \"map\"");
    }

    [Test]
    [Arguments("typeof(List<>)", "open generic")]
    [Arguments("typeof(object)", "'object' and 'dynamic'")]
    [Arguments("typeof(IShape)", "interfaces other than")]
    [Arguments("typeof(int[,])", "multi-dimensional arrays")]
    [Arguments("typeof(KeyValuePair<string, int>)", "not a transpiled class or struct")]
    [Arguments("typeof((int, string))", "not a transpiled class or struct")]
    [Arguments("typeof(SortedDictionary<string, int>)", "not a transpiled class or struct")]
    [Arguments("typeof(Hidden)", "not a transpiled class or struct")]
    [Arguments("typeof(Native)", "not a transpiled class or struct")]
    [Arguments("typeof(System.Version)", "not a transpiled class or struct")]
    [Arguments("typeof(Page<>)", "open generic")]
    [Arguments("typeof(Dictionary<int, Leaf>)", "dictionary keys other than 'string'")]
    [Arguments("typeof(List<Dictionary<Guid, Leaf>>)", "dictionary keys other than 'string'")]
    [Arguments("typeof(List<System.Version>)", "'System.Version' has no TypeScript contract")]
    [Arguments("typeof(List<IShape>)", "'TestApp.IShape' has no TypeScript contract")]
    [Arguments("typeof(Action)", "not a class or struct")]
    public async Task InvalidRoot_ReportsMs0035AtTheAttribute(string typeOf, string reason)
    {
        var diagnostics = DiagnosticsOf(
            $$"""
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public interface IShape { }

            [Transpile]
            public sealed record Page<T>(List<T> Items);

            [Ignore]
            public sealed record Hidden(string Name);

            [Transpile, Metano.Annotations.TypeScript.External]
            public sealed class Native { }

            [Transpile]
            [JsonSerializable({{typeOf}})]
            """,
            DiagnosticCodes.InvalidJsonSerializableRoot
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(diagnostics[0].Message).Contains(reason);
        await Assert.That(diagnostics[0].Location).IsNotNull();
    }

    [Test]
    public async Task InvalidRoot_IsNotEmitted()
    {
        var (files, _) = Transpile(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            [JsonSerializable(typeof(Leaf))]
            [JsonSerializable(typeof(Dictionary<int, Leaf>))]
            """
        );

        await Assert.That(files[ContextPath]).DoesNotContain("dictionaryInt32Leaf");
        await Assert.That(files[ContextPath]).Contains("get leaf(): TypeSpec<Leaf>");
    }

    [Test]
    public async Task InvalidRootListedTwice_IsReportedOnce()
    {
        var diagnostics = DiagnosticsOf(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            [JsonSerializable(typeof(Leaf))]
            [JsonSerializable(typeof(Dictionary<int, Leaf>))]
            [JsonSerializable(typeof(Dictionary<int, Leaf>))]
            """,
            DiagnosticCodes.InvalidJsonSerializableRoot
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
    }

    [Test]
    public async Task RejectedRoot_AddsNoRuntimeImport()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            [JsonSerializable(typeof(Leaf))]
            [JsonSerializable(typeof(HashSet<System.Version>))]
            """,
            allowDiagnostics: true
        );

        await Assert.That(ts).DoesNotContain("HashSet");
    }

    [Test]
    public async Task DroppedRoot_AddsNoRuntimeImport()
    {
        // The class HashSetString claims `hashSetString` first, so the
        // HashSet<string> root is dropped with an MS0036 warning.
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record HashSetString(string Name);

            [Transpile]
            [JsonSerializable(typeof(HashSetString))]
            [JsonSerializable(typeof(HashSet<string>))]
            """,
            allowDiagnostics: true
        );

        await Assert.That(ts).Contains("get hashSetString(): TypeSpec<HashSetString>");
        await Assert.That(ts).DoesNotContain("import { HashSet }");
        await Assert.That(ts).DoesNotContain("ValueSpec<HashSet<string>");
    }

    [Test]
    public async Task PackageElementTypeListedAsItsOwnRoot_IsAdmitted()
    {
        var consumer = PackageConsumer(
            """
            [JsonSerializable(typeof(Tag))]
            [JsonSerializable(typeof(List<Tag>))]
            """
        );

        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            PackageLibrary,
            consumer
        );

        var ts = files["app/json-context.ts"];

        await Assert.That(diagnostics.Where(IsJsonDiagnostic)).IsEmpty();
        await Assert.That(ts).Contains("get listTag(): ValueSpec<Tag[] | null>");
        await Assert.That(ts).Contains("spec: () => this.tag");
        await Assert.That(ts).Contains("import { Tag } from \"shared/shared\"");
    }

    [Test]
    public async Task UnreferencedRootCollision_WarnsAndDropsTheLaterRoot()
    {
        var (files, diagnostics) = Transpile(
            CollidingNestedTypes
                + """

                [Transpile]
                [JsonSerializable(typeof(Clip.Image))]
                [JsonSerializable(typeof(Anchor.Image))]
                """
        );

        var ms0036 = DiagnosticsWithCode(diagnostics, DiagnosticCodes.JsonTypeInfoNameCollision);
        var ts = files[ContextPath];

        var expectedMessage =
            "'TestApp.Anchor.Image' and 'TestApp.Clip.Image' both map to context member "
            + "'image'; 'TestApp.Clip.Image' was kept. Use TypeInfoPropertyName to "
            + "resolve the collision.";

        await Assert.That(ms0036).Count().IsEqualTo(1);
        await Assert.That(ms0036[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(ms0036[0].Message).IsEqualTo(expectedMessage);
        await Assert.That(ts).Contains("get image(): TypeSpec<Clip.Image>");
        await Assert.That(ts).DoesNotContain("TypeSpec<Anchor.Image>");
    }

    [Test]
    public async Task ReferencedLoserCollision_IsAnError()
    {
        var diagnostics = DiagnosticsOf(
            CollidingNestedTypes
                + """

                [Transpile]
                [JsonSerializable(typeof(Clip.Image))]
                [JsonSerializable(typeof(Anchor.Overlay))]
                """,
            DiagnosticCodes.JsonTypeInfoNameCollision
        );

        var expectedFragment = "'TestApp.Anchor.Image' is referenced by 'TestApp.Anchor.Overlay'";

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(diagnostics[0].Message).Contains(expectedFragment);
    }

    [Test]
    public async Task TypeInfoPropertyName_ResolvesACollision()
    {
        var ts = TranspileContext(
            CollidingNestedTypes
                + """

                [Transpile]
                [JsonSerializable(typeof(Clip.Image))]
                [JsonSerializable(typeof(Anchor.Image), TypeInfoPropertyName = "AnchorImage")]
                """
        );

        await Assert.That(ts).Contains("get image(): TypeSpec<Clip.Image>");
        await Assert.That(ts).Contains("get anchorImage(): TypeSpec<Anchor.Image>");
    }

    [Test]
    [Arguments("Options", "context member 'options'")]
    [Arguments("Resolve", "context member 'resolve'")]
    [Arguments("Converters", "context member 'converters'")]
    [Arguments("CreateSpec", "context member 'createSpec'")]
    [Arguments("CreateValueSpec", "context member 'createValueSpec'")]
    [Arguments("ReadOptions", "context member 'readOptions'")]
    [Arguments("Registry", "context field '_registry'")]
    public async Task ReservedMemberName_IsAnError(string typeName, string member)
    {
        var (files, diagnostics) = Transpile(
            $$"""
            [Transpile]
            public sealed record {{typeName}}(string Name);

            [Transpile]
            [JsonSerializable(typeof({{typeName}}))]
            """
        );

        var ms0036 = DiagnosticsWithCode(diagnostics, DiagnosticCodes.JsonTypeInfoNameCollision);

        await Assert.That(ms0036).Count().IsEqualTo(1);
        await Assert.That(ms0036[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(ms0036[0].Message).Contains(member);
        await Assert.That(files[ContextPath]).DoesNotContain($"TypeSpec<{typeName}>");
    }

    [Test]
    public async Task SameTypeWithDifferentOverrides_WarnsAndKeepsTheFirst()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            [JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]
            [JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Labels")]
            """
        );

        var ms0036 = DiagnosticsWithCode(diagnostics, DiagnosticCodes.JsonTypeInfoNameCollision);

        var expectedMessage =
            "'System.Collections.Generic.List<string>' is listed again with "
            + "TypeInfoPropertyName 'Labels'; the first TypeInfoPropertyName 'Names' "
            + "was kept.";

        await Assert.That(ms0036).Count().IsEqualTo(1);
        await Assert.That(ms0036[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(ms0036[0].Message).IsEqualTo(expectedMessage);
        await Assert.That(files[ContextPath]).Contains("get names()");
        await Assert.That(files[ContextPath]).DoesNotContain("get labels()");
    }

    private static string WrapInNamespace(string declarations, string contextDeclaration) =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{contextDeclaration}}
            """;

    private static string PackageConsumer(string roots) =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Shared;

            namespace App;

            [Transpile]
            {{roots}}
            {{ContextBoilerplate}}
            """;

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string declarations) =>
        TranspileHelper.TranspileWithDiagnostics(WrapInNamespace(declarations, ContextBoilerplate));

    private static string TranspileContext(string declarations, bool allowDiagnostics = false)
    {
        var (files, diagnostics) = Transpile(declarations);
        var jsonDiagnostics = diagnostics.Where(IsJsonDiagnostic).ToList();

        if (jsonDiagnostics.Count > 0 && !allowDiagnostics)
        {
            var formatted = string.Join("; ", jsonDiagnostics.Select(d => d.Format()));

            throw new InvalidOperationException("Unexpected diagnostics: " + formatted);
        }

        return files[ContextPath];
    }

    private static bool IsJsonDiagnostic(MetanoDiagnostic diagnostic) =>
        diagnostic.Code
            is DiagnosticCodes.InvalidJsonSerializableRoot
                or DiagnosticCodes.JsonTypeInfoNameCollision;

    private static List<MetanoDiagnostic> DiagnosticsWithCode(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string code
    ) => diagnostics.Where(diagnostic => diagnostic.Code == code).ToList();

    private static List<MetanoDiagnostic> DiagnosticsOf(string declarations, string code) =>
        DiagnosticsWithCode(Transpile(declarations).Diagnostics, code);

    private static int CountOf(string text, string fragment) => text.Split(fragment).Length - 1;
}
