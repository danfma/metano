namespace Metano.Tests;

/// <summary>
/// A discard assignment <c>_ = expr;</c> evaluates <c>expr</c> and drops the value
/// (#773): it lowers to <c>expr</c> alone, never to an assignment to an undeclared
/// <c>_</c>. A local actually named <c>_</c> keeps its assignments.
/// </summary>
public class DiscardAssignmentTests
{
    [Test]
    public async Task DiscardAssignment_EvaluatesTheExpressionOnly()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Writer { public int Value => 3; }

            [Transpile]
            public static class Uses
            {
                public static void Catch(Action a) => a();
                public static int Take(Func<int> f) => f();
                public static bool TryGet(out int v) { v = 2; return true; }

                public static int Run(Writer writer)
                {
                    Catch(() => _ = writer.Value);
                    var t = Take(() => _ = writer.Value);
                    _ = writer.Value;
                    var y = (_ = 5) + t;
                    TryGet(out _);
                    return y;
                }
            }
            """
        )["uses.ts"];

        await Assert.That(output).Contains("Uses.catch(() => {\n      writer.value;\n    });");
        await Assert.That(output).Contains("const t = Uses.take(() => writer.value);");
        await Assert.That(output).Contains("    writer.value;\n");
        await Assert.That(output).Contains("const y = (5 + t) | 0;");
        await Assert.That(output).Contains("Uses.tryGet();");
        await Assert.That(output).DoesNotContain("_ =");
    }

    [Test]
    public async Task LocalNamedUnderscore_KeepsItsAssignments()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Uses
            {
                public static int Real()
                {
                    int _ = 4;
                    _ = _ + 1;
                    return _;
                }
            }
            """
        )["uses.ts"];

        await Assert.That(output).Contains("let _ = 4;");
        await Assert.That(output).Contains("_ = (_ + 1) | 0;");
    }
}
