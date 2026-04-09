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
