namespace Metano.Tests;

/// <summary>
/// A <c>for</c> header keeps every declarator, initializer expression and
/// incrementor, and a local declaration keeps every declarator (#307, legacy
/// FR-014).
/// </summary>
public class ForLoopTranspileTests
{
    [Test]
    public async Task ForLoopsAndLocals_KeepEveryDeclaratorAndIncrementor()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Walks
            {
                public static int Converge(int n)
                {
                    var sum = 0;
                    for (int i = 0, j = n; i < j; i++, j--)
                        sum += j - i;
                    return sum;
                }

                public static int Assigned(int n)
                {
                    int k, l;
                    int total = 0, steps = 0;
                    for (k = 0, l = n; k < l; k++, l -= 2, steps++)
                        total += k;
                    return total + steps;
                }

                public static int Single(int n)
                {
                    var total = 0;
                    for (var i = 0; i < n; i++)
                        total += i;
                    return total;
                }

                public static int Forever()
                {
                    for (;;)
                        return 1;
                }
            }
            """
        );

        await Assert
            .That(result["walks.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("for-loop-multiple-clauses.ts"));
    }

    [Test]
    public async Task MultiDeclaratorLocals_PromoteEachNeverReassignedLocalToConst()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Locals
            {
                public static int Mix(int n)
                {
                    int a = 7, b = a / 2, c = 0;
                    c += a;
                    switch (n)
                    {
                        case 1:
                            int p = 1, q = 2;
                            return p + q + b + c;
                        default:
                            return c;
                    }
                }
            }
            """
        )["locals.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    const a = 7;
                    const b = intDiv(a, 2);
                    let c = 0;
                """
            );
        await Assert
            .That(output)
            .Contains(
                """
                        const p = 1;
                        const q = 2;
                """
            );
    }

    [Test]
    public async Task ForLoopDeclarators_WithAnOutArgumentCondition_StayInTheHeader()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Lookup
            {
                public static int Count(Dictionary<int, int> values)
                {
                    var hits = 0;
                    for (int k = 0, step = 1; values.TryGetValue(k, out var w); k += step, hits++)
                        hits += w;
                    return hits;
                }
            }
            """
        )["lookup.ts"];

        await Assert.That(output).Contains("for (let k = 0, step = 1; ; k += step, hits++) {");
    }

    [Test]
    public async Task ForLoopDeclarators_WithAnOutArgumentInitializer_AreBoundAheadOfTheLoop()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Lookup
            {
                public static int Walk(Dictionary<int, int> values)
                {
                    var total = 0;
                    for (int i = 0, found = values.TryGetValue(0, out var first) ? first : 0; i < found; i++)
                        total += i;
                    return total;
                }
            }
            """
        )["lookup.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let i = 0;
                      const [$found0, first] = tryGetValue(values, 0, 0);
                      const found = $found0 ? first : 0;
                      for (; ; i++) {
                """
            );
    }
}
