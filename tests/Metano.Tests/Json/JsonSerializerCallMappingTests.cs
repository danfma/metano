using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// #741: C# that serializes through a source-generated context transpiles to the
/// runtime <c>JsonSerializer</c>: <c>Ctx.Default</c> reads the emitted
/// <c>static get default()</c>, the context's <c>JsonTypeInfo</c> properties keep
/// their getter names, and the static <c>Serialize</c> / <c>Deserialize</c> calls
/// over a <c>JsonTypeInfo</c> map to <c>serializeText</c> / <c>deserializeText</c>.
/// </summary>
public class JsonSerializerCallMappingTests
{
    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string body) =>
        TranspileHelper.TranspileWithJsonSourceGenerator(
            $$"""
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            [Transpile]
            public sealed record Bag(int A, string? B);

            [Transpile]
            public sealed record Delete(int Id);

            [JsonSerializable(typeof(Bag))]
            [JsonSerializable(typeof(Delete))]
            [Transpile]
            public partial class BagContext : JsonSerializerContext;

            [Transpile]
            public static class Use
            {
            {{body}}
            }
            """
        );

    [Test]
    public async Task ContextDefault_ReadsTheStaticDefaultGetter()
    {
        var (files, _) = Transpile("public static object Run() => BagContext.Default.Delete;");

        await Assert.That(files["use.ts"]).Contains("return BagContext.default.delete;");
        await Assert.That(files["use.ts"]).DoesNotContain("default_");
    }

    [Test]
    public async Task GenericSerializeAndDeserialize_MapToTheTextApi()
    {
        var (files, diagnostics) = Transpile(
            """
            public static string Run()
            {
                var json = JsonSerializer.Serialize(new Bag(1, null), BagContext.Default.Bag);
                var back = JsonSerializer.Deserialize(json, BagContext.Default.Bag);

                return json + back;
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("import { JsonSerializer } from \"metano-runtime\";");

        await Assert
            .That(use)
            .Contains(
                "const json = JsonSerializer.serializeText(new Bag(1, null), BagContext.default.bag);"
            );

        await Assert
            .That(use)
            .Contains("const back = JsonSerializer.deserializeText(json, BagContext.default.bag);");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task NonGenericTypeInfoOverloads_MapToTheTextApi()
    {
        var (files, diagnostics) = Transpile(
            """
            public static object? Run(JsonTypeInfo info)
            {
                var json = JsonSerializer.Serialize((object)new Bag(1, null), info);

                return JsonSerializer.Deserialize(json, info);
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("JsonSerializer.serializeText(");
        await Assert.That(use).Contains("return JsonSerializer.deserializeText(json, info);");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task OptionsOverload_StaysUnmapped()
    {
        var (_, diagnostics) = Transpile(
            "public static string Run() => JsonSerializer.Serialize(new Bag(1, null), (JsonSerializerOptions?)null);"
        );

        await Assert
            .That(diagnostics)
            .Contains(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Message.Contains("System.Text.Json.JsonSerializer.Serialize")
            );
    }
}
