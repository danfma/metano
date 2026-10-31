namespace Metano.Tests;

/// <summary>
/// C# <c>null</c> stays TS <c>null</c> where a nullable value is produced (#256):
/// a native optional chain read as a value is normalized with <c>?? null</c>
/// (it yields <c>undefined</c> for a null receiver), a relational pattern on a
/// nullable value carries the implicit non-null test, a <c>[PlainObject]</c>
/// instance-method helper is imported from its owner's file, and a pattern
/// designator rebound through <c>out</c> stays <c>let</c>. Every shape was
/// checked with <c>tsgo --strict</c> and run under Bun against the C#
/// results.
/// </summary>
public class NullableResultTranspileTests
{
    [Test]
    public async Task OptionalChainReadAsValue_IsNormalizedToNull()
    {
        var output = TranspileChains(
            """
            public static int? Length(string? s) => s?.Length;
            public static void Store(Box target, Box? source) => target.Size = source?.Items.Count;
            public static int? Pass(Box? b) => Echo(b?.Items.Count);
            public static int? Echo(int? v) => v;
            public static Patch Make(Box? b) => new Patch(b?.Name, b?.Items.Count);
            public static int? Ternary(Box? b, bool f) => f ? b?.Size : null;
            public static List<int?> Sizes(List<Box?> boxes) => boxes.Select(b => b?.Size).ToList();
            public static int? Invoke(System.Func<int>? f) => f?.Invoke();
            """
        );

        await Assert.That(output).Contains("return s?.length ?? null;");
        await Assert.That(output).Contains("target.size = source?.items.length ?? null;");
        await Assert.That(output).Contains("return echo(b?.items.length ?? null);");
        await Assert
            .That(output)
            .Contains("return { note: b?.name ?? null, count: b?.items.length ?? null };");
        await Assert.That(output).Contains("return f ? b?.size ?? null : null;");
        await Assert.That(output).Contains("select((b: Box | null) => b?.size ?? null)");
        await Assert.That(output).Contains("return f?.() ?? null;");
    }

    [Test]
    public async Task OptionalChainWhereUndefinedReadsAsNull_StaysBare()
    {
        var output = TranspileChains(
            """
            public static string Fallback(Box? b) => b?.Name ?? "none";
            public static bool HasName(Box? b) => b?.Name != null;
            public static bool IsThree(Box? b) => b?.Size == 3;
            public static int? NextSize(Box? b) => b?.Next?.Size;
            public static void Discard(Box? b) => b?.Next?.Items.Clear();
            public static void Fire(System.Action? handler) => handler?.Invoke();
            public static void Later(Box b, System.Action? handler) => b.OnDone(() => handler?.Invoke());
            """
        );

        await Assert.That(output).Contains("return b?.name ?? \"none\";");
        await Assert.That(output).Contains("return b?.name != null;");
        await Assert.That(output).Contains("return b?.size === 3;");

        // The inner access is the outer one's receiver: chained, not normalized.
        await Assert.That(output).Contains("return b?.next?.size ?? null;");

        // Discarded values are never read.
        await Assert.That(output).Contains("handler?.();");
        await Assert.That(output).Contains("b.onDone(() => handler?.());");
        await Assert.That(output).DoesNotContain("handler?.() ?? null");
    }

    [Test]
    public async Task EqualityBetweenTwoChains_ComparesNormalizedValues()
    {
        // `undefined === null` is false, while C# `null == null` is true.
        var output = TranspileChains(
            "public static bool Same(Box? a, Box? b) => a?.Size == b?.Size;"
        );

        await Assert.That(output).Contains("return (a?.size ?? null) === (b?.size ?? null);");
    }

    [Test]
    public async Task RelationalPattern_OnNullableValue_TestsForNull()
    {
        var output = TranspileChains(
            """
            public static bool NonPositive(int? x) => x is <= 0;
            public static bool InRange(int? x) => x is > 0 and < 10;
            public static bool NotSmall(int? x) => x is not < 5;
            public static bool Positive(int x) => x is > 0;
            public static bool BigBox(Box b) => b is { Size: > 10 };
            """
        );

        await Assert.That(output).Contains("return !(x == null) && x <= 0;");
        await Assert.That(output).Contains("return !(x == null) && x > 0 && x < 10;");
        await Assert.That(output).Contains("return !(!(x == null) && x < 5);");
        await Assert.That(output).Contains("return x > 0;");
        await Assert.That(output).Contains("!(b.size == null) && b.size > 10");
    }

    [Test]
    public async Task PlainObjectInstanceMethod_ImportsItsHelperFromTheOwnerFile()
    {
        var files = TranspileHelper.Transpile(
            ChainsSource("public static string Upper(Item item) => item.GetTitle();")
        );

        var chains = files["chains.ts"];

        await Assert.That(chains).Contains("import { getTitle, type Item } from \"./item\";");
        await Assert.That(chains).Contains("return getTitle(item);");
    }

    [Test]
    public async Task PlainObjectHelperSharingANameWithAnotherOwner_ImportsFromItsOwnOwner()
    {
        var files = TranspileHelper.Transpile(
            ChainsSource("public static string Label(Tag tag) => tag.GetTitle();")
        );

        await Assert
            .That(files["chains.ts"])
            .Contains("import { getTitle, type Tag } from \"./tag\";");
    }

    [Test]
    public async Task SameNamedHelpersOfTwoOwnersInOneFile_ReportTheClash()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            ChainsSource(
                "public static string Both(Item item, Tag tag) => item.GetTitle() + tag.GetTitle();"
            )
        );

        await Assert
            .That(diagnostics)
            .Contains(d =>
                d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.ExtensionHelperNameClash
                && d.Message.Contains("'getTitle'")
            );
    }

    [Test]
    public async Task NestedPlainObjectHelper_IsReachedThroughItsContainer()
    {
        var files = TranspileHelper.Transpile(
            ChainsSource("public static int Twice(Outer.Inner n) => n.Dbl();")
        );

        var chains = files["chains.ts"];

        await Assert.That(chains).Contains("return Outer.dbl(n);");
        await Assert.That(chains).Contains("import { Outer } from \"./outer\";");
    }

    [Test]
    public async Task PlainObjectHelperFromAnotherPackage_ImportsFromItsSubpath()
    {
        var output = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/domain")]

            namespace Acme.Domain.Items;

            [PlainObject]
            public record Item(string Title)
            {
                public string GetTitle() => Title.ToUpperInvariant();
            }
            """,
            """
            using Acme.Domain.Items;

            [assembly: TranspileAssembly]

            namespace App;

            public static class Usage
            {
                public static string Upper(Item item) => item.GetTitle();
            }
            """
        )["app/usage.ts"];

        await Assert
            .That(output)
            .Contains("import { getTitle, type Item } from \"@acme/domain/acme/domain/items\";");
        await Assert.That(output).Contains("return getTitle(item);");
    }

    [Test]
    public async Task PatternDesignatorReboundThroughOut_IsLet()
    {
        var output = TranspileChains(
            """
            public static int Designator(object o, string s)
            {
                if (o is int n)
                {
                    int.TryParse(s, out n);
                    return n;
                }
                return -1;
            }
            """
        );

        await Assert.That(output).Contains("let n: number;");
        await Assert.That(output).Contains("n = $out0;");
    }

    private static string TranspileChains(string members) =>
        TranspileHelper.Transpile(ChainsSource(members))["chains.ts"];

    private static string ChainsSource(string members) =>
        $$"""
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using Metano.Annotations;

            [Transpile]
            public class Box
            {
                public string? Name { get; set; }
                public Box? Next { get; set; }
                public int? Size { get; set; }
                public List<int> Items { get; } = [];
                public void OnDone(System.Action done) => done();
            }

            [Transpile, PlainObject]
            public record Item(string Title)
            {
                public string GetTitle() => Title.ToUpperInvariant();
            }

            [Transpile, PlainObject]
            public record Tag(string Text)
            {
                public string GetTitle() => Text;
            }

            [Transpile, PlainObject]
            public record Patch(string? Note = null, int? Count = null);

            [Transpile]
            public class Outer
            {
                [PlainObject]
                public record Inner(int Value)
                {
                    public int Dbl() => Value * 2;
                }
            }

            [Transpile, NoContainer]
            public static class Chains
            {
            {{members}}
            }
            """;
}
