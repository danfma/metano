namespace Metano.Tests;

public class CollectionInitTests
{
    [Test]
    public async Task EmptyHashSet_GeneratesNewSet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Container
            {
                private readonly HashSet<int> _items = [];

                public Container() { }
            }
            """
        );

        var output = result["container.ts"];
        await Assert.That(output).Contains("new HashSet()");
        await Assert.That(output).DoesNotContain("[]");
    }

    [Test]
    public async Task EmptyList_StillGeneratesArray()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Container
            {
                private readonly List<int> _items = [];

                public Container() { }
            }
            """
        );

        var output = result["container.ts"];
        await Assert.That(output).Contains("[]");
        await Assert.That(output).DoesNotContain("new HashSet()");
    }
}
