namespace Metano.Tests;

/// <summary>
/// The JSON context of 64-bit integers (spec 012 FR-050, slice 012e): every 64-bit
/// property, element, dictionary key / value and root emits the
/// <c>int64</c> descriptor <c>{ kind: "int64", numberType: … }</c> — a
/// <c>[Branded]</c> type over one included — instead of a
/// <c>primitive</c> descriptor with a <c>numberType</c>, whatever the read
/// options (FR-050). A branded type keeps its companion factory
/// (<c>create</c>, the valid <c>[JsonFactory]</c> member) and its CLR
/// <c>typeName</c> on the descriptor, which the runtime applies after the
/// exact read — the factory case FR-050 does not describe.
/// </summary>
public class Int64JsonContextTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string BigIntGolden = "int64-json-context.ts";

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new JsonSerializerOptions()) { }
            protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    /// <summary>
    /// One 64-bit position of each kind: properties (signed, unsigned,
    /// nullable), array and list elements, a dictionary keyed by <c>long</c>,
    /// one keyed by <c>string</c> with a <c>ulong</c> value, a set, a plain
    /// <c>[Branded]</c> over <c>long</c> (as a member, a nullable member and
    /// a dictionary key), one with a valid <c>[JsonFactory]</c>, and the
    /// value roots.
    /// </summary>
    private const string Declarations = """
        [Transpile]
        public sealed record Snowflake(long Id, ulong Mask, long? Parent, EntityId? Origin);

        [Transpile]
        public sealed record Boxes(
            long[] Values,
            List<ulong> More,
            Dictionary<long, string> ByName,
            Dictionary<string, ulong> Counts,
            HashSet<long> Set,
            EntityId Id,
            Dictionary<EntityId, string> ByToken,
            TokenId Token
        );

        [Transpile, Branded]
        public readonly record struct EntityId(long Value);

        [Transpile, InlineWrapper]
        public readonly record struct Quota(long Value);

        [Transpile, Branded]
        public readonly record struct TokenId(long Value)
        {
            [JsonFactory]
            public static TokenId From(long value) => new TokenId(value);
        }

        [Transpile]
        [JsonSerializable(typeof(Snowflake))]
        [JsonSerializable(typeof(Boxes))]
        [JsonSerializable(typeof(EntityId))]
        [JsonSerializable(typeof(Quota))]
        [JsonSerializable(typeof(TokenId))]
        [JsonSerializable(typeof(long))]
        [JsonSerializable(typeof(ulong?))]
        """;

    private static string ContextSource() =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{Declarations}}
            {{ContextBoilerplate}}
            """;

    [Test]
    public async Task EmitsTheInt64Descriptors()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ContextSource());

        if (TranspileHelper.UpdateGoldensRequested)
        {
            TranspileHelper.WriteExpected(BigIntGolden, files[ContextPath]);

            return;
        }

        await Assert.That(files[ContextPath]).IsEqualTo(TranspileHelper.ReadExpected(BigIntGolden));

        await Assert.That(diagnostics).IsEmpty();
    }
}
