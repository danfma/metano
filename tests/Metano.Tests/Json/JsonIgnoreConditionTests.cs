using System.Text.RegularExpressions;
using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// <c>JsonIgnoreCondition.WhenWritingNull</c> in the generated context (#667,
/// spec 009 FR-046): a <c>null</c> member is left out of the JSON, whether the
/// context's <c>DefaultIgnoreCondition</c> or the member's own
/// <c>[JsonIgnore(Condition = …)]</c> says so, and a member's
/// <c>Never</c> keeps writing it under a context that omits. The runtime
/// side is <c>omitWhenNull</c> of <c>PropertySpec</c>.
///
/// Measured with a source-generated System.Text.Json context (.NET SDK
/// 10.0.401, runtime 10.0.12) over <c>Cycle(string Id, string? InferredFrom,
/// int? Count)</c> and <c>Mixed(string Id, Keep: Never, Omit: WhenWritingNull,
/// OmitDefault: WhenWritingDefault, int? Opt)</c>, every nullable member
/// <c>null</c>:
/// <code>
/// WhenWritingNull context, Cycle("cy-1")          → {"id":"cy-1"}
/// WhenWritingNull context, Cycle("cy-1", "x", 0)  → {"id":"cy-1","inferredFrom":"x","count":0}
/// WhenWritingNull context, Mixed("m")             → {"id":"m","keep":null}
/// Web context, Mixed("m")                         → {"id":"m","keep":null,"opt":null}
/// </code>
/// The same rows were written by the generated TypeScript under Bun.
/// <c>WhenWritingDefault</c> on a nullable member omits a <c>null</c> too
/// (its default); on a <c>0</c>-defaulted member it stays reported.
/// </summary>
public class JsonIgnoreConditionTests
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

    private const string Contract = """
        [Transpile]
        public sealed class Mixed
        {
            public string Id { get; set; } = "";

            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? Keep { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Omit { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string? OmitDefault { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Zero { get; set; }

            public int? Opt { get; set; }
        }
        """;

    [Test]
    public async Task ContextDefault_OmitsEveryNullableMember_ExceptANeverOne()
    {
        var (ts, diagnostics) = TranspileContext(
            "[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]"
        );

        await Assert.That(OmittingMembersOf(ts)).IsEqualTo("omit, omitDefault, opt");

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Message).Contains("'Mixed.Zero'");
    }

    [Test]
    public async Task MemberCondition_OmitsWithoutAContextDefault()
    {
        var (ts, _) = TranspileContext("");

        await Assert.That(OmittingMembersOf(ts)).IsEqualTo("omit, omitDefault");
    }

    [Test]
    public async Task NonNullableMember_IsNeverFlagged()
    {
        var (ts, _) = TranspileContext(
            "[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]"
        );

        await Assert.That(OmittingMembersOf(ts)).DoesNotContain("zero");
        await Assert.That(OmittingMembersOf(ts)).DoesNotContain("id");
    }

    private static (string Ts, IReadOnlyList<MetanoDiagnostic> Unmirrored) TranspileContext(
        string contextOptions
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{Contract}}

            [Transpile]
            {{contextOptions}}
            [JsonSerializable(typeof(Mixed))]
            {{ContextBoilerplate}}
            """
        );

        var unmirrored = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption)
            .ToList();

        return (files[ContextPath], unmirrored);
    }

    /// <summary>The <c>ts</c> names of the property specs flagged <c>omitWhenNull</c>.</summary>
    private static string OmittingMembersOf(string ts) =>
        string.Join(
            ", ",
            Regex
                .Matches(
                    ts,
                    "ts: \"([^\"]+)\"(?:(?!ts: \").)*?omitWhenNull: true",
                    RegexOptions.Singleline
                )
                .Select(match => match.Groups[1].Value)
        );
}
