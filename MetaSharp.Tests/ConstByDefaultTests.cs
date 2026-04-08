namespace MetaSharp.Tests;

/// <summary>
/// Tests proving that <c>var</c> local declarations lower to TS <c>const</c> by default
/// and only fall back to <c>let</c> when the local is mutated within its enclosing
/// scope. The detector recognizes assignment, compound assignment, prefix/postfix
/// increment/decrement, and <c>ref</c>/<c>out</c> arguments.
/// </summary>
public class ConstByDefaultTests
{
    [Test]
    public async Task ImmutableLocal_LowersToConst()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Compute()
                {
                    var x = 1 + 2;
                    return x * 10;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("const x = ");
        await Assert.That(output).DoesNotContain("let x = ");
    }

    [Test]
    public async Task ReassignedLocal_LowersToLet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Compute()
                {
                    var x = 1;
                    x = 2;
                    return x;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("let x = ");
        await Assert.That(output).DoesNotContain("const x = ");
    }

    [Test]
    public async Task CompoundAssignmentLocal_LowersToLet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Sum(int n)
                {
                    var total = 0;
                    total += n;
                    return total;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("let total = ");
    }

    [Test]
    public async Task IncrementedLocal_LowersToLet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Tick()
                {
                    var counter = 0;
                    counter++;
                    return counter;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("let counter = ");
    }

    [Test]
    public async Task LocalAssignedFromAwait_LowersToConst()
    {
        // Async case: the local is initialized from an await expression but never
        // reassigned. Should still be const.
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public class Sample
            {
                public async Task<int> LoadAsync()
                {
                    var value = await Task.FromResult(42);
                    return value + 1;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("const value = await");
    }

    [Test]
    public async Task TwoSiblingsOneMutated_LowersIndependently()
    {
        // Two locals in the same method: one mutated, one not. They should be classified
        // independently.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int Compute()
                {
                    var seed = 10;
                    var counter = 0;
                    counter++;
                    return seed + counter;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("const seed = ");
        await Assert.That(output).Contains("let counter = ");
    }
}
