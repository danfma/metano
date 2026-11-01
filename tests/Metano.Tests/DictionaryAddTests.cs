namespace Metano.Tests;

/// <summary>
/// <c>Dictionary&lt;K, V&gt;.Add(key, value)</c> throws on a duplicate key, where
/// <c>Map.set</c> overwrites, so it lowers to the <c>addEntry</c> runtime helper. The
/// indexer setter and the <c>[key] = value</c> index initializer keep overwriting through
/// <c>set</c>.
/// </summary>
public class DictionaryAddTests
{
    [Test]
    public async Task Add_OnDictionaryAndInterface_LowersToAddEntry()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Registry
            {
                private readonly Dictionary<string, int> _byName = new();

                public void Register(string name, int id) => _byName.Add(name, id);

                public void RegisterAll(IDictionary<string, int> target) => target.Add("x", 1);
            }
            """
        );

        var output = result["registry.ts"];
        await Assert.That(output).Contains("import { addEntry } from \"metano-runtime\";");
        await Assert.That(output).Contains("addEntry(this._byName, name, id);");
        await Assert.That(output).Contains("addEntry(target, \"x\", 1);");
        await Assert.That(output).DoesNotContain(".set(");
    }

    [Test]
    public async Task CollectionInitializer_AddElementThrows_IndexInitializerOverwrites()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Registry
            {
                public Dictionary<string, int> Added() => new() { { "a", 1 } };

                public Dictionary<string, int> Indexed() => new() { ["a"] = 1 };

                public void Overwrite(Dictionary<string, int> map) => map["a"] = 2;
            }
            """
        );

        var output = result["registry.ts"];
        await Assert.That(output).Contains("addEntry(receiver$temp$0, \"a\", 1);");
        await Assert.That(output).Contains(".set(\"a\", 1);");
        await Assert.That(output).Contains("map.set(\"a\", 2)");
    }
}
