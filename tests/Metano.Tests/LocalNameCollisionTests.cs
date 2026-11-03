namespace Metano.Tests;

/// <summary>
/// Local-name collision handling (#398): C# is case-sensitive but the
/// emitted local name is camelCase, so two distinct bindings can map to the
/// same emitted name. Valid C# must transpile: the binding declared later
/// (inner scope, or later in the same scope) takes a <c>$N</c> suffix at its
/// declaration and at every read; parameters keep their name (the call
/// surface) and the colliding local is renamed — an operator's parameters
/// rename too, since no TS caller names them. A local promoted by
/// <c>[ExportVarFromBody]</c> never renames; a collision the rename cannot
/// resolve (neither binding renamable) is reported — MS0006 at the entry
/// point against the attribute's argument, MS0003 anywhere else. Discard
/// parameters (<c>(_, _) =&gt;</c>) are not bindings, and sibling scopes
/// (if/else, getter vs setter, switch sections' case labels) never collide.
/// Non-colliding code is emitted exactly as before.
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
    public async Task LambdaParameterCollidingWithUnreadOuterLocal_RenamesLambdaParameter()
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

        await Assert.That(output).Contains("const [$found0, total$1] = Sample.try(s);");
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

        await Assert.That(output).Contains("static add(a: number, a$1: number): number");
        await Assert.That(output).Contains("return a + a$1;");
    }

    [Test]
    public async Task OperatorParametersWithSameEmittedName_Rename()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Money(int Cents)
            {
                public static Money operator +(Money a, Money A)
                {
                    return new Money(a.Cents + A.Cents);
                }
            }
            """
        )["money.ts"];

        // An operator is dispatched through the IR, never by a TS caller
        // naming a parameter, so its parameters rename.
        await Assert.That(output).Contains("static __add(a: Money, a$1: Money): Money");
        await Assert.That(output).Contains("a.cents + a$1.cents");
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
    public async Task DiscardLambdaParameters_AreNotBindings()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F()
                {
                    Func<int, int, int> pick = (_, _) => 3;
                    return pick(0, 0);
                }
            }
            """
        )["sample.ts"];

        // Two discard parameters are one emitted `_` plus the bridge's
        // dedup suffix — never a disambiguator `$N` rename (#398).
        await Assert.That(output).Contains("(_: number, _1: number) => 3");
        await Assert.That(output).DoesNotContain("_$1");
    }

    [Test]
    public async Task SwitchSectionLocals_ScopeToTheWholeSwitch()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int n)
                {
                    var r = 0;
                    switch (n)
                    {
                        case 1:
                            var t = 1;
                            r += t;
                            break;
                        case 2:
                            var T = 2;
                            r += T;
                            break;
                    }
                    return r;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const t = 1;");
        await Assert.That(output).Contains("const t$1 = 2;");
        await Assert.That(output).Contains("r += t$1;");
    }

    [Test]
    public async Task CaseLabelDesignators_InSiblingSections_DoNotRename()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(object o)
                {
                    var r = 0;
                    switch (o)
                    {
                        case int total:
                            r += total;
                            break;
                        case long Total:
                            r += 1;
                            break;
                    }
                    return r;
                }
            }
            """
        )["sample.ts"];

        // Each case label's bindings are wrapped in their own block, so
        // sibling sections keep the same emitted name.
        await Assert.That(output.Split("const total").Length - 1).IsEqualTo(2);
        await Assert.That(output).DoesNotContain("$1");
    }

    [Test]
    public async Task SiblingIfElseScopes_DeclaringSameName_DoNotRename()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(bool b)
                {
                    var r = 0;
                    if (b)
                    {
                        var Total = 1;
                        r += Total;
                    }
                    else
                    {
                        var total = 2;
                        r += total;
                    }
                    return r;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const total = 1;");
        await Assert.That(output).Contains("const total = 2;");
        await Assert.That(output).DoesNotContain("$1");
    }

    [Test]
    public async Task GetterAndSetter_DeclaringSameName_DoNotRename()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private int backing;

                public int Value
                {
                    get
                    {
                        var total = 1;
                        return backing + total;
                    }
                    set
                    {
                        var total = value + 1;
                        backing = total;
                    }
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const total = 1;");
        await Assert.That(output).Contains("const total = value + 1;");
        await Assert.That(output).DoesNotContain("$1");
    }

    [Test]
    public async Task PromotedPrimaryConstructorParameter_VsBodyLocal_EmitsNoCollision()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample(int total)
            {
                public int Twice()
                {
                    var Total = total + 1;
                    return Total + total;
                }
            }
            """
        )["sample.ts"];

        // The promoted parameter lowers to the private `_total` field, so
        // its reads never share an emitted name with the body local.
        await Assert.That(output).Contains("const total = this._total + 1;");
        await Assert.That(output).Contains("return total + this._total;");
        await Assert.That(output).DoesNotContain("$1");
    }

    [Test]
    public async Task ExtensionBlockReceiver_VsBodyLocal_RenamesLocal()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Ints
            {
                extension(int[] xs)
                {
                    public int Bias()
                    {
                        var Xs = 1;
                        return Xs + xs.Length;
                    }
                }
            }
            """
        )["ints.ts"];

        // The receiver is the call surface (`xs` stays); the local yields.
        await Assert.That(output).Contains("xs$1 = 1;");
        await Assert.That(output).Contains("xs$1 + xs.length");
    }

    [Test]
    public async Task ConstructorInitializerLambda_VsParameter_RenamesInInitializerAndBody()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base
            {
                public Base(Func<int, int> seed) { }
            }

            [Transpile]
            public class Sample : Base
            {
                public Sample(int total)
                    : base(Total => Total + total)
                {
                    var Total = 2;
                    total += Total;
                }
            }
            """
        )["sample.ts"];

        // One map per constructor declaration: the initializer's lambda and
        // the body's local both yield to the parameter, each in its own
        // scope, so they take consecutive suffixes.
        await Assert.That(output).Contains("total$1: number) => total$1 + total");
        await Assert.That(output).Contains("total$2 = 2;");
        await Assert.That(output).Contains("total += total$2;");
    }

    [Test]
    public async Task FieldInitializerNestedLambdas_RenameInnerParameter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static Func<int, Func<int, int>> F = Total => total => total + Total;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("total$1: number) => total$1 + total");
    }

    [Test]
    public async Task ModuleVariableInitializerNestedLambdas_RenameInnerParameter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Sample
            {
                public static Func<int, Func<int, int>> F = Total => total => total + Total;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("total$1: number) => total$1 + total");
    }

    [Test]
    public async Task InlineMemberOwnBindings_RenameInsideTheExpansion()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Calc
            {
                [Inline]
                public static int Twice(int x) => Sum(new[] { 1 }, X => X + x);

                public static int Sum(int[] xs, Func<int, int> f) => f(xs[0]);

                public static int Run() => Twice(2);
            }
            """
        )["calc.ts"];

        await Assert.That(output).Contains("x$1: number) => x$1 + x");
    }

    [Test]
    public async Task ObjectArgsParametersDifferingOnlyInCase_ReportUnresolvableDiagnostic()
    {
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int a, int A) => a + A;
            }
            """
        );

        // The keys of the public argument object cannot rename; the emitted
        // destructure would declare `a` twice, so the collision is reported.
        await Assert
            .That(diagnostics.Any(d => d.Code == "MS0003" && d.Message.Contains("'a'")))
            .IsTrue();
        await Assert.That(diagnostics.Any(d => d.Message.Contains("neither"))).IsTrue();
        _ = output;
    }

    [Test]
    public async Task ExportedLocalKeepsName_CollidingLocalRenames_NoDiagnostic()
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
        // colliding local is renamed. The rename resolves the collision, so
        // nothing is reported (#398).
        await Assert.That(program).Contains("const count = 2;");
        await Assert.That(program).Contains("count$1 = 1;");
        await Assert.That(program).Contains("count$1 + count");
        await Assert.That(program).Contains("export { count };");
        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsFalse();
    }

    [Test]
    public async Task ExportedLocalDeclaredFirst_CollidingLocalRenames_NoDiagnostic()
    {
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("count")]
                public static void Main()
                {
                    var count = 1;
                    var Count = 2;
                    System.Console.WriteLine(count + Count);
                }
            }
            """
        );

        var program = output["program.ts"];

        await Assert.That(program).Contains("const count = 1;");
        await Assert.That(program).Contains("count$1 = 2;");
        await Assert.That(program).Contains("count + count$1");
        await Assert.That(program).Contains("export { count };");
        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsFalse();
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

    public const string GoldenSource = """
        [Transpile]
        public class Collisions
        {
            public static int ParamVsLocal(int total)
            {
                var Total = 10;
                return Total + total;
            }

            public static int TwoLocals()
            {
                var Total = 1;
                var total = 2;
                return Total + total;
            }

            public static int LambdaOverLocal()
            {
                var Factor = 3;
                Func<int, int> scale = factor => factor * Factor;
                return scale(2);
            }

            public static int ForEachVsLocal(int[] items)
            {
                var Total = 0;
                foreach (var total in items)
                {
                    Total += total;
                }
                return Total;
            }

            public static int CatchVsLocal()
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

            public static int DesignatorVsLocal(object o)
            {
                var Total = 1;
                if (o is int total)
                {
                    return Total + total;
                }
                return Total;
            }

            public static int SwitchSectionLocals(int n)
            {
                var r = 0;
                switch (n)
                {
                    case 1:
                        var t = 1;
                        r += t;
                        break;
                    case 2:
                        var T = 2;
                        r += T;
                        break;
                }
                return r;
            }
        }
        """;

    [Test]
    public async Task Golden_LocalNameCollisions()
    {
        var output = TranspileHelper.Transpile(GoldenSource)["collisions.ts"];

        await Assert
            .That(output)
            .IsEqualTo(TranspileHelper.ReadExpected("local-name-collisions.ts"));
    }
}
