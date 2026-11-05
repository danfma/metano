using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// The runtime-helper import lines of a generated <c>SerializerContext</c>:
/// the requirement set must cover every runtime value the context spells —
/// the entry types in <c>ValueSpec&lt;T&gt;</c> / <c>TypeSpec&lt;T&gt;</c>, the
/// types the factory casts (<c>p.items as HashSet&lt;number&gt;</c>), and the
/// temporal parse sites (<c>parse: Temporal.PlainDate.from</c>). System.Text.Json's
/// source generator stays out of the compilation (the explicit context body),
/// so its synthesized members cannot hide the emitted file (#496 review).
/// </summary>
public class JsonContextImportTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private static string Context(string declarations) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Metano.Annotations;

            namespace TestApp;

            {{declarations}}

            [Transpile]
            [JsonSerializable(typeof(Hub))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

    [Test]
    public async Task MemberOnlyTemporal_ContextImportsTemporal()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                public sealed record Day(DateOnly When);

                [Transpile]
                public sealed record Hub(string Title, Day Busiest);
                """
            )
        )[ContextPath];

        await Assert.That(ts).Contains("""import { Temporal } from "@js-temporal/polyfill";""");
    }

    [Test]
    public async Task PolymorphicVariantTemporalMember_ContextImportsTemporal()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
                [JsonDerivedType(typeof(Leaf), "leaf")]
                public abstract record Node;

                [Transpile]
                public sealed record Leaf(DateOnly Day) : Node;

                [Transpile]
                public sealed record Hub(Node Root);
                """
            )
        )[ContextPath];

        await Assert.That(ts).Contains("""import { Temporal } from "@js-temporal/polyfill";""");
    }

    [Test]
    public async Task NoTemporalDescriptor_ContextOmitsTemporalImport()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                public sealed record Hub(string Title, int Count);
                """
            )
        )[ContextPath];

        await Assert.That(ts).DoesNotContain("from \"@js-temporal/polyfill\"");
        await Assert.That(ts).Contains("p.count as number");
    }

    [Test]
    public async Task HashSetMember_FactoryCastImportsHashSet()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                public sealed record Hub(string Title, HashSet<int> Tags);
                """
            )
        )[ContextPath];

        // The factory cast spells the runtime type: `p.tags as HashSet<number>`.
        await Assert.That(ts).Contains("p.tags as HashSet<number>");
        await Assert.That(ts).Contains("""import { HashSet } from "metano-runtime";""");
    }

    [Test]
    public async Task AbstractClassEntry_FactoryThrowsWithoutCastingImports()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                public abstract class Base
                {
                    public HashSet<int> Tags { get; init; } = new();
                }

                [Transpile]
                public sealed record Hub(string Title, Base Ranking);
                """
            )
        )[ContextPath];

        // Base's factory only throws; nothing casts the set, so the import
        // would dangle unused (#496).
        await Assert.That(ts).DoesNotContain("import { HashSet");
        await Assert.That(ts).Contains("factory: () => {");
        await Assert
            .That(ts)
            .Contains("""throw new Error("Cannot construct abstract type 'Base'.");""");
    }

    [Test]
    public async Task AbstractRecordWithSetMember_FactoryThrowsWithoutCastingImports()
    {
        var ts = TranspileHelper.Transpile(
            Context(
                """
                [Transpile]
                public abstract record Base(HashSet<int> Tags);

                [Transpile]
                public sealed record Hub(string Title, Base Ranking);
                """
            )
        )[ContextPath];

        await Assert.That(ts).DoesNotContain("import { HashSet");
        await Assert.That(ts).Contains("factory: () => {");
    }
}
