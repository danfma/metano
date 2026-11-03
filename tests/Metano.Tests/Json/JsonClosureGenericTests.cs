using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// Closed generic object types in the contract closure of the generated
/// <c>SerializerContext</c> (spec 009 FR-033): reached through a property, a
/// value root or a <c>base</c> link, or listed as a root, each gets its FR-031
/// getter and a <c>TypeSpec</c> typed with its type arguments.
/// </summary>
public class JsonClosureGenericTests
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

    private const string FeedDeclarations = """
        [Transpile]
        public sealed record EpisodeSummary(string Title, int Number);

        [Transpile]
        public sealed record Page<T>(IReadOnlyList<T> Items, int Total);

        [Transpile]
        public sealed record Feed(Page<EpisodeSummary> Latest);

        [Transpile]
        [JsonSerializable(typeof(Feed))]
        """;

    [Test]
    public async Task GenericPropertyType_MatchesGolden()
    {
        var ts = TranspileContext(FeedDeclarations);
        var expected = TranspileHelper.ReadExpected("json-closure-generic-context.ts");

        await Assert.That(ts).IsEqualTo(expected);
    }

    [Test]
    public async Task GenericRoot_IsAdmittedWithItsConstructedName()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Page<T>(List<T> Items);

            [Transpile]
            [JsonSerializable(typeof(Page<Leaf>))]
            [JsonSerializable(typeof(Page<string>))]
            """
        );

        await Assert.That(ts).Contains("get pageLeaf(): TypeSpec<Page<Leaf>>");
        await Assert.That(ts).Contains("get pageString(): TypeSpec<Page<string>>");
        await Assert.That(ts).Contains("spec: () => this.leaf");
        await Assert.That(ts).Contains("new Page(p.items as Leaf[])");
        await Assert.That(ts).Contains("new Page(p.items as string[])");
    }

    [Test]
    public async Task GenericElementOfValueRoot_IsDiscovered()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Leaf(string Name);

            [Transpile]
            public sealed record Page<T>(List<T> Items);

            [Transpile]
            [JsonSerializable(typeof(List<Page<Leaf>>))]
            """
        );

        await Assert.That(ts).Contains("get listPageLeaf(): ValueSpec<Page<Leaf>[] | null>");
        await Assert.That(ts).Contains("spec: () => this.pageLeaf");
        await Assert.That(ts).Contains("get pageLeaf(): TypeSpec<Page<Leaf>>");
        await Assert.That(ts).Contains("get leaf(): TypeSpec<Leaf>");
    }

    [Test]
    public async Task GenericBase_IsLinkedByItsConstructedName()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public abstract record Box<T>(T Value);

            [Transpile]
            public sealed record IntBox(int Value, string Label) : Box<int>(Value);

            [Transpile]
            [JsonSerializable(typeof(IntBox))]
            """
        );

        await Assert.That(ts).Contains("base: this.boxInt32");
        await Assert.That(ts).Contains("get boxInt32(): TypeSpec<Box<number>>");
        await Assert.That(ts).Contains("type: Box,");
    }

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string declarations) =>
        TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}
            {{ContextBoilerplate}}
            """
        );

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = Transpile(declarations);

        var jsonDiagnostics = diagnostics
            .Where(diagnostic =>
                diagnostic.Code
                    is DiagnosticCodes.InvalidJsonSerializableRoot
                        or DiagnosticCodes.JsonTypeInfoNameCollision
            )
            .ToList();

        if (jsonDiagnostics.Count > 0)
            throw new InvalidOperationException(
                "Unexpected diagnostics: "
                    + string.Join("; ", jsonDiagnostics.Select(d => d.Format()))
            );

        return files[ContextPath];
    }
}
