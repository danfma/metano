namespace Metano.Tests;

public class LookupMappingTests
{
    [Test]
    public async Task ILookup_TypeIndexerCountContains_747()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Linq;
            using System.Collections.Generic;

            [Transpile]
            public static class Lookups
            {
                public static ILookup<string, int> Build(List<int> xs) => xs.ToLookup(x => x.ToString());

                public static int Use(ILookup<string, int> lookup, string key) =>
                    lookup[key].Count() + lookup.Count + (lookup.Contains(key) ? 1 : 0);
            }
            """
        );

        var output = result["lookups.ts"];

        var import = "import type { Lookup } from \"metano-runtime\";";
        var signature = "static build(xs: List<number>): Lookup<string, number>";

        await Assert.That(output).Contains(import);
        await Assert.That(output).Contains(signature);
        await Assert.That(output).Contains("linq(lookup.get(key), count()) + lookup.count");
        await Assert.That(output).Contains("lookup.contains(key)");
    }
}
