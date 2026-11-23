namespace Metano.Tests;

/// <summary>
/// A <c>: this(...)</c> chain into a <c>params</c> constructor packs its
/// expanded arguments into the array C# builds, an empty one when it passes
/// none, and forwards an array passed in normal form as it is (#379).
/// </summary>
public class ConstructorChainParamsTests
{
    [Test]
    public async Task ExpandedParamsArguments_ArePackedIntoTheArray()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Bag
            {
                public int[] Xs;
                public string Tag;

                public Bag(string tag, params int[] xs)
                {
                    Tag = tag;
                    Xs = xs;
                }

                public Bag() : this("none") { }

                public Bag(int a) : this("one", a, a + 1, a + 2) { }

                public Bag(int[] arr, bool b) : this("arr", arr) { }
            }
            """
        )["bag.ts"];

        await Assert.That(output).Contains("const xs = [] as number[];");

        await Assert
            .That(output)
            .Contains("const xs = [__chain0Arg1, __chain0Arg2, __chain0Arg3];");

        await Assert.That(output).Contains("const xs = __chain0Arg1;");
        await Assert.That(output).DoesNotContain("const xs = undefined;");
    }
}
