using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #752: a context's <c>WriteIndented</c>, <c>IndentCharacter</c>, <c>IndentSize</c> and
/// <c>NewLine</c> reach the generated context as a <c>writeOptions</c> getter, which
/// <c>serializeText</c> reads; a value System.Text.Json rejects warns <c>MS0037</c>.
/// </summary>
public class JsonWriteOptionsTests
{
    private static (string Ts, IReadOnlyList<MetanoDiagnostic> Diagnostics) TranspileContext(
        string options
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            public record Todo(string Title);

            [Transpile]
            [JsonSourceGenerationOptions({{options}})]
            [JsonSerializable(typeof(Todo))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """
        );

        return (files["test-app/json-context.ts"], diagnostics);
    }

    private static IEnumerable<MetanoDiagnostic> Unmirrored(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption);

    [Test]
    public async Task WriteIndented_EmitsTheWriteOptionsGetter()
    {
        var (ts, diagnostics) = TranspileContext("WriteIndented = true");

        await Assert.That(ts).Contains("protected override get writeOptions(): JsonWriteOptions");
        await Assert.That(ts).Contains("return { writeIndented: true };");
        await Assert.That(Unmirrored(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task IndentMembers_EmitOnlyTheNonDefaultOnes()
    {
        var (ts, diagnostics) = TranspileContext(
            "WriteIndented = true, IndentCharacter = '\\t', IndentSize = 1, NewLine = \"\\r\\n\""
        );

        await Assert.That(ts).Contains("writeIndented: true,");
        await Assert.That(ts).Contains("indentCharacter: \"\\t\",");
        await Assert.That(ts).Contains("indentSize: 1,");
        await Assert.That(ts).Contains("newLine: \"\\r\\n\",");
        await Assert.That(Unmirrored(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task DefaultIndentValues_EmitNoMemberForThem()
    {
        var (ts, _) = TranspileContext(
            "WriteIndented = true, IndentCharacter = ' ', IndentSize = 2, NewLine = \"\\n\""
        );

        await Assert.That(ts).Contains("return { writeIndented: true };");
    }

    [Test]
    [Arguments("")]
    [Arguments("WriteIndented = false")]
    [Arguments("IndentSize = 4, NewLine = \"\\r\\n\"")]
    public async Task CompactContext_GetsNoWriteOptions(string options)
    {
        var (ts, _) = TranspileContext(options);

        await Assert.That(ts).DoesNotContain("writeOptions");
    }

    [Test]
    [Arguments("IndentCharacter = 'x'", "IndentCharacter")]
    [Arguments("IndentSize = 128", "IndentSize")]
    [Arguments("NewLine = \"\\r\"", "NewLine")]
    public async Task ValueSystemTextJsonRejects_WarnsAndKeepsTheDefault(
        string options,
        string option
    )
    {
        var (ts, diagnostics) = TranspileContext($"WriteIndented = true, {options}");

        await Assert.That(Unmirrored(diagnostics).Single().Message).StartsWith($"'{option}'");
        await Assert.That(ts).Contains("return { writeIndented: true };");
    }
}
