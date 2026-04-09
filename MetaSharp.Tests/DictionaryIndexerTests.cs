namespace MetaSharp.Tests;

/// <summary>
/// Tests for the Dictionary indexer lowering. Dictionary&lt;K,V&gt; lowers to JS Map
/// at the type level, but bracket access (<c>dict[key]</c>) doesn't work on Map —
/// it needs <c>map.get(key)</c> for read and <c>map.set(key, value)</c> for write.
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
            """);

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
            """);

        var output = result["cache.ts"];
        await Assert.That(output).Contains("this._items.set(key, value)");
        await Assert.That(output).DoesNotContain("this._items[key] =");
    }

    [Test]
    public async Task ArrayIndexer_StillBracket()
    {
        // Sanity check: arrays / lists keep the bracket form because JS arrays
        // support it natively. Only dictionaries need the method-call rewrite.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Bag
            {
                private readonly List<int> _items = [];
                public int Read(int idx) => _items[idx];
            }
            """);

        var output = result["bag.ts"];
        await Assert.That(output).Contains("this._items[idx]");
        await Assert.That(output).DoesNotContain("this._items.get");
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
            """);

        await Assert.That(result["cache.ts"]).Contains("map.get(key)");
    }

    // ─── TryGetValue pattern expansion ──────────────────────

    [Test]
    public async Task TryGetValue_ExpandsToConstAndIf()
    {
        // The canonical pattern: `if (dict.TryGetValue(key, out var value)) { use(value); }`
        // expands to two TS statements at the body level.
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
            """);

        var output = result["cache.ts"];
        // const value = this._items.get(key);
        await Assert.That(output).Contains("const value = this._items.get(key)");
        // if (value !== undefined) {
        await Assert.That(output).Contains("value !== undefined");
        await Assert.That(output).Contains("return value;");
        // No TryGetValue call in the output
        await Assert.That(output).DoesNotContain("tryGetValue");
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
            """);

        var output = result["cache.ts"];
        await Assert.That(output).Contains("const found = this._items.get(key)");
        await Assert.That(output).Contains("found !== undefined");
        await Assert.That(output).Contains("return found;");
        await Assert.That(output).Contains("return \"default\";");
    }

    [Test]
    public async Task TryGetValue_NegatedCondition_NotExpanded()
    {
        // `if (!dict.TryGetValue(...))` is a negated form that doesn't match the
        // expansion pattern. It falls through as a regular invocation (the method
        // isn't mapped, so it stays as-is / falls through to the default handler).
        // We don't expand this case because the value isn't usable in the then-branch.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cache
            {
                private readonly Dictionary<string, int> _items = new();
                public bool Missing(string key) => !_items.ContainsKey(key);
            }
            """);

        // Sanity check: ContainsKey → has
        await Assert.That(result["cache.ts"]).Contains("!this._items.has(key)");
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
            """);

        await Assert.That(result["cache.ts"]).Contains("map.get(key)");
    }
}
