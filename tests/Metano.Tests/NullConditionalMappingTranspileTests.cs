namespace Metano.Tests;

/// <summary>
/// Member mappings applied through C# null-conditional access (#248). A step
/// TypeScript's <c>?.</c> can carry (a plain or renamed property, method or
/// element access) stays native optional chaining; a step mapped to a
/// template, a receiver-taking helper or a wrapped receiver guards the
/// receiver explicitly, so the null short-circuit is never lost.
/// </summary>
public class NullConditionalMappingTranspileTests
{
    private const string OrderSource = """
        #nullable enable
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        [Transpile]
        public static class OrderExtensions
        {
            public static int Twice(this List<int> items) => items.Count * 2;
        }

        [Transpile]
        public class Order
        {
            public List<int> Items { get; } = new();
            public Order? Next { get; set; }
            public Order? GetNext() => Next;
        }
        """;

    private static string TranspileHelperBody(string members) =>
        TranspileHelper.Transpile(
            OrderSource
                + "\n[Transpile, NoContainer]\npublic static class Helper\n{\n"
                + members
                + "\n}\n"
        )["helper.ts"];

    [Test]
    public async Task TrimThroughNullConditional_FallsBackToEmptyString()
    {
        var output = TranspileHelperBody(
            "public static string Normalize(string? value) => value?.Trim() ?? string.Empty;"
        );

        await Assert.That(output).Contains("return value?.trim() ?? \"\";");
    }

    [Test]
    public async Task ListCountThroughNullConditional_MapsToOptionalLength()
    {
        var output = TranspileHelperBody(
            "public static int? Size(List<int>? items) => items?.Count;"
        );

        await Assert.That(output).Contains("return items?.length;");
    }

    [Test]
    public async Task TemplateMappedContinuation_GuardsTheReceiver()
    {
        var output = TranspileHelperBody(
            "public static void Clear(Order? order) => order?.Items.Clear();"
        );

        await Assert.That(output).Contains("order == null ? null : order.items.length = 0;");
        await Assert.That(output).DoesNotContain("order?.items.length = 0");
    }

    [Test]
    public async Task HelperMappedContinuation_GuardsTheReceiverAndArguments()
    {
        var output = TranspileHelperBody(
            "public static bool? Remove(Order? order, int item) => order?.Items.Remove(item);"
        );

        await Assert
            .That(output)
            .Contains("return order == null ? null : listRemove(order.items, item);");
        await Assert.That(output).DoesNotContain("listRemove(order?.items");
    }

    [Test]
    public async Task ExtensionMethodContinuation_RewritesToGuardedHelperCall()
    {
        var output = TranspileHelperBody(
            "public static int? Doubled(Order? order) => order?.Items.Twice();"
        );

        await Assert.That(output).Contains("return order == null ? null : twice(order.items);");
    }

    [Test]
    public async Task LinqContinuation_RewritesToGuardedPipeline()
    {
        var output = TranspileHelperBody(
            "public static int? Large(Order? order) => order?.Items.Where(i => i > 1).Count();"
        );

        await Assert.That(output).Contains("return order == null ? null : linq(order.items, ");
    }

    [Test]
    public async Task PlainContinuation_StaysInsideOneOptionalChain()
    {
        var output = TranspileHelperBody(
            """
            public static int? Third(Order? order) => order?.Next.Next.Items.Count;
            public static int? Head(Order? order) => order?.Items[0];
            public static string? Tail(string? value) => value?.Substring(1).Trim();
            """
        );

        await Assert.That(output).Contains("return order?.next.next.items.length;");
        await Assert.That(output).Contains("return order?.items[0];");
        await Assert.That(output).Contains("return value?.substring(1).trim();");
    }

    [Test]
    public async Task ImpureReceiver_IsEvaluatedOnceThroughAnUntypedBinding()
    {
        var output = TranspileHelperBody(
            "public static bool? RemoveNext(Order order, int item) => order.GetNext()?.Items.Remove(item);"
        );

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => receiver$temp$0 == null ? null : listRemove(receiver$temp$0.items, item))(order.getNext())"
            );
        await Assert.That(output).DoesNotContain(": any");
        await Assert.That(output).DoesNotContain("undefined");
    }

    [Test]
    public async Task AwaitInsideGuardedBinding_MakesTheBindingAsync()
    {
        var output = TranspileHelperBody(
            "public static async Task<bool?> RemoveLater(Order order, Task<int> item) => order.GetNext()?.Items.Remove(await item);"
        );

        await Assert
            .That(output)
            .Contains(
                "await (async (receiver$temp$0) => receiver$temp$0 == null ? null : listRemove(receiver$temp$0.items, await item))(order.getNext())"
            );
    }

    [Test]
    public async Task NullConditionalGuards_MatchGolden()
    {
        var output = TranspileHelperBody(
            """
            public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
            public static void Clear(Order? order) => order?.Items.Clear();
            public static bool? Remove(Order? order, int item) => order?.Items.Remove(item);
            public static void ClearNext(Order? order) => order?.GetNext()?.Items.Clear();
            public static int? Third(Order? order) => order?.Next.Next.Items.Count;
            public static async Task<bool?> RemoveLater(Order order, Task<int> item) =>
                order.GetNext()?.Items.Remove(await item);
            """
        );

        await Assert
            .That(output)
            .IsEqualTo(TranspileHelper.ReadExpected("null-conditional-guards.ts"));
    }
}
