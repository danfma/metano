namespace Metano.Tests;

/// <summary>
/// Member mappings applied through C# null-conditional access (#248). A step
/// TypeScript's <c>?.</c> can carry (a plain or renamed property, method or
/// element access) stays native optional chaining; a step lowered to a
/// template, a receiver-taking helper or a wrapped receiver guards the
/// receiver explicitly, so the null short-circuit is never lost. The leading
/// step after <c>?.</c> gets the same lowering as any later one.
/// </summary>
public class NullConditionalMappingTranspileTests
{
    [Test]
    public async Task TrimThroughNullConditional_FallsBackToEmptyString()
    {
        var output = TranspileInHelperClass(
            "public static string Normalize(string? value) => value?.Trim() ?? string.Empty;"
        );

        await Assert.That(output).Contains("return value?.trim() ?? \"\";");
    }

    [Test]
    public async Task ListCountThroughNullConditional_MapsToOptionalLength()
    {
        var output = TranspileInHelperClass(
            "public static int? Size(List<int>? items) => items?.Count;"
        );

        await Assert.That(output).Contains("return items?.length;");
    }

    [Test]
    public async Task NonNullReceiver_IsNotGuarded()
    {
        var output = TranspileInHelperClass(
            "public static bool Remove(Order order, int item) => order.Items.Remove(item);"
        );

        await Assert.That(output).Contains("return listRemove(order.items, item);");
    }

    [Test]
    public async Task HelperMappedContinuation_GuardsTheReceiverAndArguments()
    {
        var output = TranspileInHelperClass(
            "public static bool? Remove(Order? order, int item) => order?.Items.Remove(item);"
        );

        await Assert
            .That(output)
            .Contains("return order == null ? null : listRemove(order.items, item);");
    }

    [Test]
    public async Task ExtensionMethodContinuation_RewritesToGuardedHelperCall()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Doubled(Order? order) => order?.Items.Twice();
            public static int? NextDoubled(Order? order) => order?.Next?.Items.Twice();
            """
        );

        await Assert.That(output).Contains("return order == null ? null : twice(order.items);");

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : twice($receiver.items))(order?.next);"
            );
    }

    [Test]
    public async Task LinqContinuation_RewritesToGuardedPipeline()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Large(Order? order) => order?.Items.Where(i => i > 1).Count();
            public static int? LargeItems(List<int>? items) => items?.Where(i => i > 1).Count();
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return order == null ? null : linq(order.items, where((i: number) => i > 1), count());"
            );

        await Assert
            .That(output)
            .Contains(
                "return items == null ? null : linq(items, where((i: number) => i > 1), count());"
            );
    }

    [Test]
    public async Task LeadingExtensionStep_RewritesToGuardedHelperCall()
    {
        var output = TranspileInHelperClass(
            """
            public static int? ScoreOf(Order? order) => order?.Score();
            public static string? ScoreText(Order? order) => order?.Score().ToString();
            public static int? Doubled(List<int>? items) => items?.Twice();
            """
        );

        await Assert.That(output).Contains("return order == null ? null : score(order);");

        await Assert
            .That(output)
            .Contains("return order == null ? null : score(order).toString();");

        await Assert.That(output).Contains("return items == null ? null : twice(items);");
    }

    [Test]
    public async Task LeadingExtensionBlockMembers_RewriteToGuardedHelperCalls()
    {
        var output = TranspileInHelperClass(
            """
            public static int? TotalOf(Order? order) => order?.Total;
            public static int? BonusOf(Order? order) => order?.Bonus();
            public static int? NextTotal(Order? order) => order?.Next?.Total;
            """
        );

        await Assert.That(output).Contains("return order == null ? null : total$get(order);");
        await Assert.That(output).Contains("return order == null ? null : bonus(order);");

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : total$get($receiver))(order?.next);"
            );
    }

    [Test]
    public async Task LeadingEmitStep_SubstitutesTheReceiver()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Size(Order? order) => order?.Size();
            public static string? SizeText(Order? order) => order?.Size().ToString();
            """
        );

        await Assert.That(output).Contains("return order == null ? null : order.items.length;");

        await Assert
            .That(output)
            .Contains("return order == null ? null : order.items.length.toString();");

        await Assert.That(output).DoesNotContain("$0");
    }

    [Test]
    public async Task ElementBinding_LowersToOptionalElementAccess()
    {
        var output = TranspileInHelperClass(
            """
            public static int? First(List<int>? items) => items?[0];
            public static int? FirstLength(List<string>? names) => names?[0].Length;
            public static int? Lookup(Dictionary<string, int>? scores) => scores?["k"];
            """
        );

        await Assert.That(output).Contains("return items?.[0];");
        await Assert.That(output).Contains("return names?.[0].length;");
        await Assert.That(output).Contains("return scores?.get(\"k\");");
    }

    [Test]
    public async Task IndexFromEndAndRange_ThroughNullConditional()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Last(int[]? items) => items?[^1];
            public static int[]? Tail(int[]? items) => items?[1..];
            public static int? LastItem(Order? order) => order?.Items[^1];
            public static char? LastChar(string? value) => value?.Substring(1)[^1];
            public static string? Middle(string? value) => value?.Substring(1)[1..^1];
            """
        );

        await Assert.That(output).Contains("return items?.[items.length - 1];");
        await Assert.That(output).Contains("return items?.slice(1);");
        await Assert.That(output).Contains("return order?.items[order.items.length - 1];");

        await Assert
            .That(output)
            .Contains(
                "return value == null ? null : ((receiver$temp$0) => receiver$temp$0[receiver$temp$0.length - 1])(value.substring(1));"
            );

        await Assert
            .That(output)
            .Contains(
                "return value == null ? null : ((receiver$temp$0) => receiver$temp$0.slice(1, receiver$temp$0.length - 1))(value.substring(1));"
            );
    }

    [Test]
    public async Task ImpureReceiverReadBeyondTheChainRoot_KeepsItsBinding()
    {
        var output = TranspileInHelperClass(
            """
            public static int[]? Find(int[]? items) => items;
            public static int? Last(int[]? items) => Find(items)?[^1];
            public static int[]? AllButLast(int[]? items) => Find(items)?[..^1];
            public static int? LastItem(Order order) => order.GetNext()?.Items[^1];
            public static int? Newest(Order order) => order.GetNext()?.Items.Newest();
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : $receiver[$receiver.length - 1])(find(items));"
            );

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : $receiver.slice(0, $receiver.length - 1))(find(items));"
            );

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : $receiver.items[$receiver.items.length - 1])(order.getNext());"
            );

        await Assert.That(output).DoesNotContain("?.[$receiver");
        await Assert.That(output).DoesNotContain("?.items[$receiver");
        await Assert.That(output).DoesNotContain("?.slice(0, $receiver");
    }

    [Test]
    public async Task InlineBodyWithNestedNullConditional_BindsEachReceiverInItsScope()
    {
        var output = TranspileInHelperClass(
            "public static int? NextNewestOf(Order order) => order.GetNext()?.NextNewest();"
        );

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : (($receiver) => $receiver == null ? null : $receiver.items[$receiver.items.length - 1])($receiver.next))(order.getNext());"
            );
    }

    [Test]
    public async Task PlainContinuation_StaysInsideOneOptionalChain()
    {
        var output = TranspileInHelperClass(
            """
            public static int? Third(Order? order) => order?.Self.Self.Items.Count;
            public static int? Head(Order? order) => order?.Items[0];
            public static string? Tail(string? value) => value?.Substring(1).Trim();
            """
        );

        await Assert.That(output).Contains("return order?.self.self.items.length;");
        await Assert.That(output).Contains("return order?.items[0];");
        await Assert.That(output).Contains("return value?.substring(1).trim();");
    }

    [Test]
    public async Task ImpureReceiver_IsEvaluatedOnceThroughAnUntypedBinding()
    {
        var output = TranspileInHelperClass(
            "public static bool? RemoveNext(Order order, int item) => order.GetNext()?.Items.Remove(item);"
        );

        await Assert
            .That(output)
            .Contains(
                "return (($receiver) => $receiver == null ? null : listRemove($receiver.items, item))(order.getNext());"
            );
    }

    [Test]
    public async Task AwaitInsideGuardedBinding_MakesTheBindingAsync()
    {
        var output = TranspileInHelperClass(
            "public static async Task<bool?> RemoveLater(Order order, Task<int> item) => order.GetNext()?.Items.Remove(await item);"
        );

        await Assert
            .That(output)
            .Contains(
                "return await (async ($receiver) => $receiver == null ? null : listRemove($receiver.items, await item))(order.getNext());"
            );
    }

    [Test]
    public async Task GuardInStatementPosition_LowersToIf()
    {
        var output = TranspileInHelperClass(
            """
            public static void Clear(Order? order) => order?.Items.Clear();
            public static async Task RemoveLater(Order order, Task<int> item)
            {
                order.GetNext()?.Items.Remove(await item);
            }
            """
        );

        await Assert
            .That(output)
            .Contains("if (order != null) {\n    order.items.length = 0;\n  }");

        await Assert
            .That(output)
            .Contains(
                "const $receiver = order.getNext();\n\n    if ($receiver != null) {\n      listRemove($receiver.items, await item);\n    }"
            );

        await Assert.That(output).DoesNotContain("? null :");
    }

    // Native optional chains (`value?.trim()`, `order?.self.self.items.length`)
    // yield `undefined` for a null receiver, while the guarded forms yield
    // `null`; a declared `T | null` result therefore fails `tsc --strict` on
    // the native forms. Known deviation tracked by #256.
    [Test]
    public async Task NullConditionalGuards_MatchGolden()
    {
        var output = TranspileInHelperClass(
            """
            public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
            public static void Clear(Order? order) => order?.Items.Clear();
            public static bool? Remove(Order? order, int item) => order?.Items.Remove(item);
            public static void ClearNext(Order? order) => order?.GetNext()?.Items.Clear();
            public static int? Third(Order? order) => order?.Self.Self.Items.Count;
            public static int? ScoreOf(Order? order) => order?.Score();
            public static int? First(List<int>? items) => items?[0];
            public static void ClearNextItems(Order? order) => order?.Next?.Items.Clear();
            public static bool? RemoveFromNext(Order? order, int item) => order?.Next?.Items.Remove(item);
            public static async Task<bool?> RemoveLater(Order order, Task<int> item) =>
                order.GetNext()?.Items.Remove(await item);
            """
        );

        await Assert
            .That(output)
            .IsEqualTo(TranspileHelper.ReadExpected("null-conditional-guards.ts"));
    }

    private static string TranspileInHelperClass(string members) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [Transpile]
            public static class OrderExtensions
            {
                public static int Twice(this List<int> items) => items.Count * 2;
                public static int Score(this Order order) => order.Items.Count;

                [Inline(InlineMode.Substitute)]
                public static int Newest(this List<int> items) => items[items.Count - 1];

                [Inline(InlineMode.Substitute)]
                public static int? NextNewest(this Order order) => order.Next?.Items[^1];

                extension(Order order)
                {
                    public int Total => order.Items.Count;
                    public int Bonus() => order.Items.Count + 2;
                }
            }

            [Transpile]
            public class Order
            {
                public List<int> Items { get; } = [];
                public Order? Next { get; set; }
                public Order Self => this;
                public Order? GetNext() => Next;

                [Emit("$0.items.length")]
                public int Size() => Items.Count;
            }

            [Transpile, NoContainer]
            public static class Helper
            {
            {{members}}
            }
            """
        )["helper.ts"];
}
