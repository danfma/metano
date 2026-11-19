namespace Metano.Tests;

/// <summary>
/// <c>Dictionary&lt;K, V&gt;.Add(key, value)</c> throws on a duplicate key, where
/// <c>set</c> overwrites, so it lowers to the runtime dictionary's throwing <c>add</c>
/// member (<c>Dictionary.of</c> in an <c>{ k, v }</c> initializer). The indexer setter and
/// the <c>[key] = value</c> index initializer (<c>Dictionary.assign</c>) keep overwriting.
/// </summary>
public class DictionaryAddTests
{
    [Test]
    public async Task Add_OnDictionaryAndInterface_LowersToTheThrowingAdd()
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

        await Assert
            .That(output)
            .Contains(
                "import { Dictionary, type IDictionary } from \"metano-runtime/system/collections\";"
            );

        await Assert.That(output).Contains("this._byName.add(name, id);");
        await Assert.That(output).Contains("target.add(\"x\", 1);");
        await Assert.That(output).DoesNotContain("addEntry");
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
        await Assert.That(output).Contains("return Dictionary.of([\n      [\"a\", 1],\n    ]);");

        await Assert
            .That(output)
            .Contains("return Dictionary.assign([\n      [\"a\", 1],\n    ]);");

        await Assert.That(output).Contains("map.set(\"a\", 2)");
    }
}
