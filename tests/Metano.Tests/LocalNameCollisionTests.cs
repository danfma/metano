namespace Metano.Tests;

/// <summary>
/// Local-name collision handling (#398): C# is case-sensitive but the
/// emitted local name is camelCase, so two distinct bindings can map to the
/// same emitted name. Valid C# must transpile: the binding declared later
/// (inner scope, or later in the same scope) takes a <c>$N</c> suffix at its
/// declaration and at every read; parameters keep their name (the call
/// surface) and the colliding local is renamed; a local promoted by
/// <c>[ExportVarFromBody]</c> is a module export, so a collision there is
/// reported (MS0006) instead of renaming the export. Non-colliding code is
/// emitted exactly as before.
/// </summary>
public class LocalNameCollisionTests
{
    [Test]
    public async Task ParameterAndLocalWithSameEmittedName_RenamesLocal()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Sum(int total)
                {
                    var Total = 10;
                    return Total + total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const total$1 = 10;");
        await Assert.That(output).Contains("return total$1 + total;");
        await Assert.That(output).DoesNotContain("const total = 10;");
    }

    [Test]
    public async Task TwoLocalsWithSameEmittedName_RenamesLaterLocal()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Sum()
                {
                    var Total = 1;
                    var total = 2;
                    return Total + total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const total = 1;");
        await Assert.That(output).Contains("const total$1 = 2;");
        await Assert.That(output).Contains("return total + total$1;");
    }

    [Test]
    public async Task LambdaParameterReadsCollidingOuterLocal_RenamesLambdaParameter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Scale()
                {
                    var Factor = 3;
                    Func<int, int> scale = factor => factor * Factor;
                    return scale(2);
                }
            }
            """
        )["sample.ts"];

        // The lambda parameter is renamed (a parameter never shadows a
        // still-referenced outer local), keeping the outer Factor reads as
        // plain `factor` — the wrong-value bug of #398.
        await Assert.That(output).Contains("const factor = 3;");
        await Assert.That(output).Contains("factor$1: number) => factor$1 * factor");
    }

    [Test]
    public async Task LambdaParameterCollidingWithUnreadOuterLocal_StillRenames()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Scale()
                {
                    var Factor = 3;
                    Func<int, int> scale = factor => factor + 1;
                    return scale(Factor);
                }
            }
            """
        )["sample.ts"];

        // Chosen policy: every inner binding that collides with a visible
        // outer emitted name is renamed, read or not (simpler, still correct).
        await Assert.That(output).Contains("factor$1: number) => factor$1 + 1");
        await Assert.That(output).Contains("scale(factor)");
    }

    [Test]
    public async Task ForEachVariableCollidingWithLocal_RenamesForEachVariable()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Sum(int[] items)
                {
                    var Total = 0;
                    foreach (var total in items)
                    {
                        Total += total;
                    }
                    return Total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("let total = 0;");
        await Assert.That(output).Contains("const total$1 of items");
        await Assert.That(output).Contains("total += total$1;");
        await Assert.That(output).Contains("return total;");
    }

    [Test]
    public async Task CatchVariableCollidingWithLocal_RenamesCatchVariable()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F()
                {
                    var Ex = 0;
                    try
                    {
                        Ex = 1;
                    }
                    catch (Exception ex)
                    {
                        return Ex + ex.Message.Length;
                    }
                    return Ex;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("catch (ex$1)");
        await Assert.That(output).Contains("ex + ex$1");
    }

    [Test]
    public async Task PatternDesignatorCollidingWithLocal_RenamesDesignator()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(object o)
                {
                    var Total = 1;
                    if (o is int total)
                    {
                        return Total + total;
                    }
                    return Total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("return total + total$1;");
        await Assert.That(output).DoesNotContain("total + total;");
    }

    [Test]
    public async Task OutVarCollidingWithLocal_RenamesOutVar()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(string s)
                {
                    var Total = 0;
                    if (Try(s, out var total))
                    {
                        Total = total;
                    }
                    return Total;
                }

                private static bool Try(string s, out int value)
                {
                    value = s.Length;
                    return true;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("total$1");
        await Assert.That(output).Contains("total = total$1;");
    }

    [Test]
    public async Task TwoParametersWithSameEmittedName_RenamesLaterParameter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Add(int a, int A)
                {
                    return a + A;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("a$1");
        await Assert.That(output).Contains("return a + a$1;");
    }

    [Test]
    public async Task ReservedWordEscapeCollision_RenamesLaterLocal()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F()
                {
                    var Default = 1;
                    var @default = 2;
                    return Default + @default;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const default_ = 1;");
        await Assert.That(output).Contains("default$1 = 2;");
        await Assert.That(output).Contains("return default_ + default$1;");
    }

    [Test]
    public async Task ExportedLocalCollision_ReportsDiagnosticAndKeepsExportName()
    {
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("count")]
                public static void Main()
                {
                    var Count = 1;
                    var count = 2;
                    System.Console.WriteLine(Count + count);
                }
            }
            """
        );

        var program = output["program.ts"];

        // The exported local keeps its name (it is the module surface); the
        // colliding earlier local is renamed, and the collision is reported.
        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
        await Assert.That(program).Contains("const count = 2;");
        await Assert.That(program).Contains("count$1 = 1;");
        await Assert.That(program).Contains("count$1 + count");
        await Assert.That(program).Contains("export { count };");
    }

    [Test]
    public async Task RenamedLocalCapturedAndMutatedInsideClosure_UsesRenamedName()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F()
                {
                    var Total = 0;
                    Action bump = () =>
                    {
                        Total++;
                    };
                    var total = 1;
                    bump();
                    return Total + total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("let total = 0;");
        await Assert.That(output).Contains("total++;");
        await Assert.That(output).Contains("total$1 = 1;");
        await Assert.That(output).Contains("return total + total$1;");
    }

    [Test]
    public async Task NonCollidingLocals_EmitPlainCamelCaseUnchanged()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Sum(int[] xs)
                {
                    var Total = 0;
                    foreach (var x in xs)
                    {
                        Total += x;
                    }
                    return Total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("let total = 0;");
        await Assert.That(output).Contains("const x of xs");
        await Assert.That(output).Contains("total += x;");
        await Assert.That(output).DoesNotContain("$1");
    }
}
