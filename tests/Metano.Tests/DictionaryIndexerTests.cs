namespace Metano.Tests;

/// <summary>
/// Tests for the Dictionary indexer lowering. Dictionary&lt;K,V&gt; lowers to the
/// runtime <c>Dictionary</c>, whose indexer is <c>get(key)</c> for a read and
/// <c>set(key, value)</c> for a write.
/// </summary>
public class DictionaryIndexerTests
{
    [Test]
    public async Task DictionaryGet_LowersToMapGet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, int> _items = new();
                public int Read(string key) => _items[key];
            }
            """
        );

        var output = result["cache.ts"];
        await Assert.That(output).Contains("this._items.get(key)");
        await Assert.That(output).DoesNotContain("this._items[key]");
    }

    [Test]
    public async Task DictionarySet_LowersToMapSet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, int> _items = new();
                public void Write(string key, int value) => _items[key] = value;
            }
            """
        );

        var output = result["cache.ts"];
        await Assert.That(output).Contains("this._items.set(key, value)");
        await Assert.That(output).DoesNotContain("this._items[key] =");
    }

    [Test]
    public async Task ListIndexer_LowersToGet()
    {
        // A List<T> is the runtime List, whose indexer is get(i) as well.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Bag
            {
                private readonly List<int> _items = [];
                public int Read(int idx) => _items[idx];
            }
            """
        );

        var output = result["bag.ts"];
        await Assert.That(output).Contains("this._items.get(idx)");
        await Assert.That(output).DoesNotContain("this._items[idx]");
    }

    [Test]
    public async Task IDictionaryGet_AlsoLowers()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                public int Read(IDictionary<string, int> map, string key) => map[key];
            }
            """
        );

        await Assert.That(result["cache.ts"]).Contains("map.get(key)");
    }

    // ─── TryGetValue (see OutArgumentTranspileTests) ─────────

    [Test]
    public async Task TryGetValue_BindsResultAndValueBeforeTheIf()
    {
        // `if (dict.TryGetValue(key, out var value)) { use(value); }` binds the result
        // and the value in one destructuring, then tests the result.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, int> _items = new();
                public int? Lookup(string key)
                {
                    if (_items.TryGetValue(key, out var value))
                    {
                        return value;
                    }
                    return null;
                }
            }
            """
        );

        var output = result["cache.ts"];

        await Assert
            .That(output)
            .Contains("const [$found0, value] = this._items.tryGetValue(key, 0);");

        await Assert.That(output).Contains("if ($found0) {");
        await Assert.That(output).Contains("return value;");
    }

    [Test]
    public async Task TryGetValue_WithElse_PreservesElseBranch()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, string> _items = new();
                public string Get(string key)
                {
                    if (_items.TryGetValue(key, out var found))
                        return found;
                    else
                        return "default";
                }
            }
            """
        );

        var output = result["cache.ts"];

        await Assert
            .That(output)
            .Contains("const [$found0, found] = this._items.tryGetValue(key, null);");

        await Assert.That(output).Contains("if ($found0) {");
        await Assert.That(output).Contains("return found;");
        await Assert.That(output).Contains("return \"default\";");
    }

    [Test]
    public async Task TryGetValue_NegatedCondition_NotExpanded()
    {
        // Sanity check for the presence test the negated TryGetValue forms build
        // on; `if (!dict.TryGetValue(...))` itself is covered by
        // OutVariableTranspileTests.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, int> _items = new();
                public bool Missing(string key) => !_items.ContainsKey(key);
            }
            """
        );

        // Sanity check: ContainsKey → containsKey
        await Assert.That(result["cache.ts"]).Contains("!this._items.containsKey(key)");
    }

    [Test]
    public async Task IReadOnlyDictionaryGet_AlsoLowers()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                public int Read(IReadOnlyDictionary<string, int> map, string key) => map[key];
            }
            """
        );

        await Assert.That(result["cache.ts"]).Contains("map.get(key)");
    }
}
