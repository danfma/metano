namespace Metano.Tests;

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

    [Test]
    public async Task LocalReassignedInsideWithOrSwitchArm_LowersToLet()
    {
        // The reassignment walk reaches `with` values, collection elements and
        // switch-expression arms, so these locals are not promoted to const.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X);

            [Transpile]
            public class Sample
            {
                public int Compute(Point p, int k)
                {
                    var a = 0;
                    var moved = p with { X = a++ };
                    var b = 0;
                    var items = new[] { b += 1 };
                    var c = 0;
                    var picked = k switch { 1 => c = 5, _ => 0 };
                    return a + b + c + moved.X + items.Length + picked;
                }
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("let a = ");
        await Assert.That(output).Contains("let b = ");
        await Assert.That(output).Contains("let c = ");
    }

    [Test]
    public async Task NullInitializedNullableLocal_CastsInitializerToDeclaredType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Holder
            {
                public int Get() => 1;
            }

            [Transpile]
            public class Sample
            {
                public int? Read(bool flag)
                {
                    Holder? n = null;
                    Holder? m = null;

                    if (flag)
                        m = new Holder();

                    return n?.Get() ?? m?.Get();
                }
            }
            """
        );

        var output = result["sample.ts"];

        await Assert.That(output).Contains("const n = null as Holder | null;");
        await Assert.That(output).Contains("let m = null as Holder | null;");
        await Assert.That(output).Contains("n?.get()");
    }

    [Test]
    public async Task DefaultInitializedNullableReferenceLocal_CastsInitializer()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public string? Read()
                {
                    string? a = default;
                    return a;
                }
            }
            """
        );

        var output = result["sample.ts"];

        await Assert.That(output).Contains("const a = null as string | null;");
    }

    [Test]
    public async Task NullInitializedNullableValueTypeLocal_CastsInitializer()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int? Read()
                {
                    int? b = null;
                    return b;
                }
            }
            """
        );

        var output = result["sample.ts"];

        await Assert.That(output).Contains("const b = null as number | null;");
    }

    [Test]
    public async Task NullableLocalWithNonNullInitializer_StaysUncast()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public int? Read()
                {
                    int? b = 5;
                    return b;
                }
            }
            """
        );

        var output = result["sample.ts"];

        await Assert.That(output).Contains("const b = 5;");
        await Assert.That(output).DoesNotContain(" as number | null");
    }

    [Test]
    public async Task NonNullableNullForgivingLocal_StaysUncast()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public Sample Read()
                {
                    Sample h = null!;
                    return h;
                }
            }
            """
        );

        var output = result["sample.ts"];

        await Assert.That(output).Contains("const h = null;");
    }
}
