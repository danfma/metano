namespace Metano.Tests;

/// <summary>
/// Lowering of <c>string.Join(separator, values)</c>
/// (<c>src/Metano/Runtime/Strings.cs</c>) onto <c>Array.prototype.join</c>. JS has no
/// <c>String.join</c>, so the extractor gathers the values into one array argument:
/// array-shaped values pass through, expanded <c>params</c> become an array literal and
/// other enumerables are spread into one.
/// </summary>
public class StringJoinMappingTests
{
    [Test]
    public async Task ArrayShapedValues_JoinDirectly()
    {
        var output = TranspileText(
            """
            public static string Names(string[] names) => string.Join(", ", names);
            public static string Ids(List<int> ids) => string.Join(",", ids);
            public static string Items(IReadOnlyList<string> items) => string.Join("; ", items);
            """
        );

        await Assert.That(output).Contains("return names.join(\", \");");
        await Assert.That(output).Contains("return ids.join(\",\");");
        await Assert.That(output).Contains("return items.join(\"; \");");
        await Assert.That(output).DoesNotContain("String.join");
    }

    [Test]
    public async Task ExpandedParams_JoinAnArrayLiteral()
    {
        var output = TranspileText(
            """
            public static string Pair() => string.Join("-", "a", "b");
            public static string Mixed(char separator, object value) => string.Join(separator, value, 1, "x");
            """
        );

        await Assert.That(output).Contains("return [\"a\", \"b\"].join(\"-\");");
        await Assert.That(output).Contains("return [value, 1, \"x\"].join(separator);");
        await Assert.That(output).DoesNotContain("String.join");
    }

    [Test]
    public async Task OtherEnumerables_AreSpreadIntoAnArray()
    {
        var output = TranspileText(
            """
            public static string Lazy(IEnumerable<string> values) => string.Join("-", values);
            public static string Unique(HashSet<string> values) => string.Join(", ", values);
            public static string Filtered(IEnumerable<int> values) => string.Join("|", values.Where(v => v > 1));
            """
        );

        await Assert.That(output).Contains("return [...values].join(\"-\");");
        await Assert.That(output).Contains("return [...values].join(\", \");");
        await Assert
            .That(output)
            .Contains("return [...linq(values, where((v: number) => v > 1))].join(\"|\");");
        await Assert.That(output).DoesNotContain("String.join");
    }

    [Test]
    public async Task CompositeArrayExpression_IsGroupedBeforeJoin()
    {
        var output = TranspileText(
            """
            public static string Fallback(string[]? primary, string[] backup) => string.Join(",", primary ?? backup);
            """
        );

        await Assert.That(output).Contains("return (primary ?? backup).join(\",\");");
    }

    private static string TranspileText(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class Text
            {
            {{members}}
            }
            """
        );

        return result["text.ts"];
    }
}
