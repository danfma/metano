namespace Metano.Tests;

/// <summary>
/// Lowering of the dictionary <c>Keys</c> / <c>Values</c> views and of dictionary
/// counts (<c>src/Metano/Runtime/Dictionaries.cs</c>). JS <c>Map.keys()</c> /
/// <c>Map.values()</c> are one-shot iterators, so a view lowers to an array copy, while
/// <c>dict.Keys.Count</c>, <c>dict.Values.Count</c> and <c>dict.Keys.Contains(k)</c>
/// fold into <c>dict.size</c> and <c>dict.has(k)</c> without building the copy.
/// </summary>
public class DictionaryViewMappingTests
{
    [Test]
    public async Task ViewCounts_FoldIntoMapSize()
    {
        var output = TranspileViews(
            """
            public static int Keys(Dictionary<string, int> d) => d.Keys.Count;
            public static int Values(Dictionary<string, int> d) => d.Values.Count;
            public static int InterfaceKeys(IDictionary<string, int> d) => d.Keys.Count;
            public static int InterfaceValues(IDictionary<string, int> d) => d.Values.Count;
            """
        );

        await Assert
            .That(output)
            .Contains("static keys(d: Map<string, number>): number {\n    return d.size;");
        await Assert
            .That(output)
            .Contains("static values(d: Map<string, number>): number {\n    return d.size;");
        await Assert
            .That(output)
            .Contains("static interfaceKeys(d: Map<string, number>): number {\n    return d.size;");
        await Assert
            .That(output)
            .Contains(
                "static interfaceValues(d: Map<string, number>): number {\n    return d.size;"
            );
        await Assert.That(output).DoesNotContain("d.keys");
        await Assert.That(output).DoesNotContain("d.values");
    }

    [Test]
    public async Task InterfaceDictionaryCount_MapsToMapSize()
    {
        // Roslyn binds these to the inherited ICollection<KeyValuePair<K,V>>.Count /
        // IReadOnlyCollection<…>.Count, whose own mapping is the array `length`.
        var output = TranspileViews(
            """
            public static int Mutable(IDictionary<string, int> d) => d.Count;
            public static int ReadOnly(IReadOnlyDictionary<string, int> d) => d.Count;
            public static int Concrete(Dictionary<string, int> d) => d.Count;
            """
        );

        await Assert
            .That(output)
            .Contains("static mutable(d: Map<string, number>): number {\n    return d.size;");
        await Assert
            .That(output)
            .Contains("static readOnly(d: Map<string, number>): number {\n    return d.size;");
        await Assert
            .That(output)
            .Contains("static concrete(d: Map<string, number>): number {\n    return d.size;");
        await Assert.That(output).DoesNotContain("d.length");
    }

    [Test]
    public async Task KeysContains_FoldsIntoMapHas()
    {
        var output = TranspileViews(
            """
            public static bool Concrete(Dictionary<string, int> d, string key) => d.Keys.Contains(key);
            public static bool Mutable(IDictionary<string, int> d, string key) => d.Keys.Contains(key);
            """
        );

        await Assert
            .That(output)
            .Contains(
                "static concrete(d: Map<string, number>, key: string): boolean {\n    return d.has(key);"
            );
        await Assert
            .That(output)
            .Contains(
                "static mutable(d: Map<string, number>, key: string): boolean {\n    return d.has(key);"
            );
    }

    [Test]
    public async Task ReadOnlyKeysContains_ScansTheArrayCopyThroughLinq()
    {
        // IReadOnlyDictionary<K,V>.Keys is an IEnumerable<K>, so Contains is the LINQ
        // extension: it scans the array copy, which gives the same answer.
        var output = TranspileViews(
            """
            public static bool ReadOnly(IReadOnlyDictionary<string, int> d, string key) => d.Keys.Contains(key);
            """
        );

        await Assert.That(output).Contains("return linq([...d.keys()], contains(key));");
    }

    [Test]
    public async Task Views_LowerToArrayCopies()
    {
        var output = TranspileViews(
            """
            public static Dictionary<string, int>.KeyCollection Keys(Dictionary<string, int> d) => d.Keys;
            public static Dictionary<string, int>.ValueCollection Values(Dictionary<string, int> d) => d.Values;
            public static ICollection<string> MutableKeys(IDictionary<string, int> d) => d.Keys;
            public static ICollection<int> MutableValues(IDictionary<string, int> d) => d.Values;
            public static IEnumerable<string> ReadOnlyKeys(IReadOnlyDictionary<string, int> d) => d.Keys;
            public static IEnumerable<int> ReadOnlyValues(IReadOnlyDictionary<string, int> d) => d.Values;
            """
        );

        await Assert
            .That(output)
            .Contains("static keys(d: Map<string, number>): string[] {\n    return [...d.keys()];");
        await Assert
            .That(output)
            .Contains(
                "static values(d: Map<string, number>): number[] {\n    return [...d.values()];"
            );
        await Assert
            .That(output)
            .Contains(
                "static mutableKeys(d: Map<string, number>): string[] {\n    return [...d.keys()];"
            );
        await Assert
            .That(output)
            .Contains(
                "static mutableValues(d: Map<string, number>): number[] {\n    return [...d.values()];"
            );
        await Assert
            .That(output)
            .Contains(
                "static readOnlyKeys(d: Map<string, number>): Iterable<string> {\n    return [...d.keys()];"
            );
        await Assert
            .That(output)
            .Contains(
                "static readOnlyValues(d: Map<string, number>): Iterable<number> {\n    return [...d.values()];"
            );
    }

    [Test]
    public async Task HeldViews_UseArrayMembers()
    {
        var output = TranspileViews(
            """
            public static int Score(Dictionary<string, int> d)
            {
                var keys = d.Keys;
                var values = d.Values;
                return keys.Count + values.Count + (keys.Contains("a") ? 1 : 0);
            }
            """
        );

        await Assert.That(output).Contains("const keys = [...d.keys()];");
        await Assert.That(output).Contains("const values = [...d.values()];");
        await Assert
            .That(output)
            .Contains("return keys.length + values.length + (keys.includes(\"a\") ? 1 : 0);");
    }

    [Test]
    public async Task Views_FeedLoopsLinqAndJoin()
    {
        var output = TranspileViews(
            """
            public static int Total(IReadOnlyDictionary<string, int> d)
            {
                var total = 0;
                foreach (var value in d.Values)
                    total += value;
                return total + d.Values.Sum();
            }
            public static List<string> Names(Dictionary<string, int> d) => d.Keys.ToList();
            public static bool HasValue(Dictionary<string, int> d) => d.Values.Contains(2);
            public static string Joined(Dictionary<string, int> d) => string.Join(",", d.Keys);
            """
        );

        await Assert.That(output).Contains("for (const value of [...d.values()]) {");
        await Assert.That(output).Contains("return total + linq([...d.values()], sum());");
        await Assert.That(output).Contains("return linq([...d.keys()], toArray());");
        await Assert.That(output).Contains("return linq([...d.values()], contains(2));");
        await Assert.That(output).Contains("return [...d.keys()].join(\",\");");
    }

    [Test]
    public async Task CompositeDictionaryExpression_IsGrouped()
    {
        var output = TranspileViews(
            """
            public static int Count(Dictionary<string, int>? primary, Dictionary<string, int> backup) =>
                (primary ?? backup).Keys.Count;
            public static string[] Keys(Dictionary<string, int>? primary, Dictionary<string, int> backup) =>
                (primary ?? backup).Keys.ToArray();
            """
        );

        await Assert.That(output).Contains("return (primary ?? backup).size;");
        await Assert
            .That(output)
            .Contains("return linq([...(primary ?? backup).keys()], toArray());");
    }

    [Test]
    public async Task DerivedDictionary_FoldsThroughTheInheritedViews()
    {
        var output = TranspileViews(
            """
            public static int Count(Bag bag) => bag.Keys.Count;
            public static bool Has(Bag bag) => bag.Keys.Contains("a");
            """
        );

        await Assert.That(output).Contains("return bag.size;");
        await Assert.That(output).Contains("return bag.has(\"a\");");
    }

    [Test]
    public async Task UserTypeKeysProperty_IsNotFolded()
    {
        // The folds key on the BCL dictionary that declares the view, not on the
        // member names: a user type's own Keys list keeps its list mappings.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Index
            {
                public List<string> Keys { get; } = new();

                public int Size(Index other) => other.Keys.Count;

                public bool Has(Index other, string key) => other.Keys.Contains(key);
            }
            """
        );

        var output = result["index.ts"];

        await Assert.That(output).Contains("return other.keys.length;");
        await Assert.That(output).Contains("return other.keys.includes(key);");
        await Assert.That(output).DoesNotContain("other.size");
        await Assert.That(output).DoesNotContain("other.has(");
    }

    private static string TranspileViews(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            public class Bag : Dictionary<string, int> { }

            [Transpile]
            public static class Views
            {
            {{members}}
            }
            """
        );

        return result["views.ts"];
    }
}
