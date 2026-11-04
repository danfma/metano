using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The <c>valueType</c> flag of the generated <c>SerializerContext</c>
/// (FR-037, slice S3b): every C# value type except <c>Nullable&lt;T&gt;</c>
/// marks its spec, so JSON <c>null</c> at that root throws as in
/// System.Text.Json, while reference types read it as <c>null</c>.
/// </summary>
public class JsonNullRootTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string GoldenDeclarations = """
        [Transpile]
        public readonly record struct SPoint(int X, int Y);

        [Transpile]
        public record struct Corner(int X);

        [Transpile]
        public sealed record Segment(SPoint From, SPoint? To, Corner Tip);

        [Transpile]
        public enum Level { Low, High }

        [Transpile]
        public static class Outer
        {
            [Transpile]
            public readonly record struct Inner(int V);
        }

        [Transpile]
        public readonly record struct Pair<T>(T A);

        [Transpile]
        [JsonSerializable(typeof(SPoint))]
        [JsonSerializable(typeof(SPoint?))]
        [JsonSerializable(typeof(Segment))]
        [JsonSerializable(typeof(bool))]
        [JsonSerializable(typeof(DateOnly))]
        [JsonSerializable(typeof(bool?))]
        [JsonSerializable(typeof(Level))]
        [JsonSerializable(typeof(Outer.Inner))]
        [JsonSerializable(typeof(Pair<int>))]
        """;

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task NullRoots_MatchGolden()
    {
        var ts = TranspileContext();

        await Assert.That(ts).IsEqualTo(TranspileHelper.ReadExpected("json-context-null-roots.ts"));
    }

    [Test]
    public async Task StructContract_ReachedOnlyThroughAProperty_IsMarked()
    {
        var ts = TranspileContext();

        await Assert.That(SpecOf(ts, "corner")).Contains("valueType: true");
    }

    [Test]
    public async Task ClassContract_IsNotMarked()
    {
        var ts = TranspileContext();

        await Assert.That(SpecOf(ts, "segment")).DoesNotContain("valueType");
    }

    [Test]
    public async Task ValueTypeRoots_AreMarked()
    {
        var ts = TranspileContext();

        await Assert.That(SpecOf(ts, "boolean")).Contains("valueType: true");
        await Assert.That(SpecOf(ts, "dateOnly")).Contains("valueType: true");
    }

    [Test]
    public async Task ValueTypeSpecs_NameTheirClrType()
    {
        var ts = TranspileContext();

        await Assert.That(SpecOf(ts, "boolean")).Contains("typeName: \"System.Boolean\"");
        await Assert.That(SpecOf(ts, "level")).Contains("typeName: \"TestApp.Level\"");
        await Assert.That(SpecOf(ts, "sPoint")).Contains("typeName: \"TestApp.SPoint\"");
        await Assert.That(SpecOf(ts, "inner")).Contains("typeName: \"TestApp.Outer+Inner\"");

        await Assert
            .That(SpecOf(ts, "pairInt32"))
            .Contains("typeName: \"TestApp.Pair`1[System.Int32]\"");
    }

    [Test]
    public async Task NullableRoots_AreNotMarked()
    {
        var ts = TranspileContext();

        await Assert.That(SpecOf(ts, "nullableSPoint")).DoesNotContain("valueType");
        await Assert.That(SpecOf(ts, "nullableBoolean")).DoesNotContain("valueType");
    }

    /// <summary>The getter body of <paramref name="getter"/>, up to its closing brace.</summary>
    private static string SpecOf(string ts, string getter)
    {
        var start = ts.IndexOf($"  get {getter}()", StringComparison.Ordinal);

        if (start < 0)
            throw new InvalidOperationException($"The context has no getter '{getter}'.");

        var end = ts.IndexOf("\n  }\n", start, StringComparison.Ordinal);

        return ts[start..end];
    }

    private static string TranspileContext()
    {
        var source = $$"""
            using System;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{GoldenDeclarations}}

            {{ContextBoilerplate}}
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error).ToList();

        if (errors.Count > 0)
        {
            var formatted = string.Join("; ", errors.Select(d => d.Format()));

            throw new InvalidOperationException("Unexpected diagnostics: " + formatted);
        }

        return files[ContextPath];
    }
}
