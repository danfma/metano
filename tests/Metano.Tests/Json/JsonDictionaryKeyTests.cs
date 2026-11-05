using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// The key descriptor of a dictionary property (#479, spec 009 FR-030): the
/// value descriptor of the key type, plus what the runtime needs to parse a
/// JSON property name into it as System.Text.Json does. A numeric key always
/// names its <c>numberType</c>, whatever the context's read options, and so
/// does a numeric enum key for its underlying type. A <c>bool</c>, enum,
/// <c>Guid</c> or temporal key names its CLR <c>typeName</c>; a
/// <c>string</c>, <c>decimal</c> or <c>[Branded]</c> key keeps the value
/// descriptor. The golden is <c>json-dictionary-keys.ts</c>.
/// </summary>
public class JsonDictionaryKeyTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public enum Level { Low, High }

        [Transpile]
        public enum Shade : byte { Light, Dark }

        [Transpile, StringEnum]
        public enum Mood { Calm, Loud }

        [Transpile, Branded]
        public readonly record struct Tag(string Value);

        [Transpile]
        public sealed record Keys(
            Dictionary<int, string> ByInt,
            Dictionary<long, string> ByLong,
            Dictionary<short, string> ByShort,
            Dictionary<byte, string> ByByte,
            Dictionary<uint, string> ByUInt,
            Dictionary<double, string> ByDouble,
            Dictionary<bool, string> ByFlag,
            Dictionary<Level, string> ByLevel,
            Dictionary<Shade, string> ByShade,
            Dictionary<Mood, string> ByMood,
            Dictionary<Guid, string> ById,
            Dictionary<DateOnly, string> ByDay,
            Dictionary<decimal, string> ByPrice,
            Dictionary<string, string> ByName,
            Dictionary<Tag, string> ByTag
        );

        // The DateOnly root brings the Temporal import: the context imports
        // the runtime helpers of its roots' own types, not of their members.
        [Transpile]
        [JsonSerializable(typeof(Keys))]
        [JsonSerializable(typeof(DateOnly))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task DictionaryKeys_MatchGolden()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);
        var expected = TranspileHelper.ReadExpected("json-dictionary-keys.ts");

        await Assert.That(JsonDiagnosticsOf(diagnostics)).IsEmpty();

        await Assert.That(files[ContextPath]).IsEqualTo(expected);
    }

    [Test]
    public async Task NumericKey_UnderNumbersFromStrings_NamesItsNumberTypeOnce()
    {
        var source = Source.Replace(
            "[JsonSerializable(typeof(Keys))]",
            "[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]"
                + "[JsonSerializable(typeof(Keys))]"
        );

        // Guard: the Replace above must match, or the test runs the unmodified source.
        await Assert.That(source).IsNotEqualTo(Source);

        var (files, _) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert
            .That(files[ContextPath])
            .Contains("""key: { kind: "primitive", numberType: "System.Int32" },""");
    }

    private static List<MetanoDiagnostic> JsonDiagnosticsOf(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(diagnostic =>
                diagnostic.Code
                    is DiagnosticCodes.InvalidJsonSerializableRoot
                        or DiagnosticCodes.JsonTypeInfoNameCollision
            )
            .ToList();
}
