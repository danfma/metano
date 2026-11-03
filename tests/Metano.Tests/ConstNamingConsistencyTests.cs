namespace Metano.Tests;

/// <summary>
/// Naming policy for <c>const</c> bindings (#390): a local — a <c>const</c> local
/// included — is camelCased at its declaration and at every read, escaping JS
/// reserved words like any other local; a module-scope compile-time constant
/// (a C# <c>const</c> field of a containerless static class) keeps its C# name
/// verbatim, at the declaration and at every read (same file, cross-file and
/// cross-package); a <c>const</c> field of a regular class is a data member and
/// stays camelCase; a <c>[Name]</c> override always wins verbatim. Whatever the
/// rule, the declaration and every reference must resolve to the same emitted
/// name — the bug these tests pin was a declaration under the C# name and reads
/// under the camelCased one.
/// </summary>
public class ConstNamingConsistencyTests
{
    [Test]
    public async Task ConstLocal_ReadsOnceAndTwice_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int Once(int x)
                {
                    const int K = 3;
                    return K + x;
                }

                public static int Twice(int x)
                {
                    const int K = 3;
                    return K + x + K;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const k = 3;");
        await Assert.That(output).Contains("return k + x;");
        await Assert.That(output).Contains("return k + x + k;");
        await Assert.That(output).DoesNotContain("K");
    }

    [Test]
    public async Task ConstLocalInsideLambda_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int x)
                {
                    Func<int, int> scale = n =>
                    {
                        const int Factor = 3;
                        return Factor * n + x;
                    };
                    return scale(2);
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const factor = 3;");
        await Assert.That(output).Contains("return factor * n + x;");
        await Assert.That(output).DoesNotContain("Factor");
    }

    [Test]
    public async Task ConstLocalInsideSwitchSection_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int x)
                {
                    switch (x)
                    {
                        case 1:
                            const int Bonus = 10;
                            return Bonus + x;
                        default:
                            return x;
                    }
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const bonus = 10;");
        await Assert.That(output).Contains("return bonus + x;");
        await Assert.That(output).DoesNotContain("Bonus");
    }

    [Test]
    public async Task ConstLocalInsideConstructorAccessorAndEntryPoint_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private readonly int _seed;
                private int _total;

                public Sample()
                {
                    const int Base = 1;
                    _seed = Base * 2;
                }

                public int Total
                {
                    get
                    {
                        const int Rounding = 1;
                        return _total + Rounding;
                    }
                    set { _total = value; }
                }

                public int F(int x)
                {
                    const int Step = 2;
                    _total = x;
                    return Total + Step;
                }
            }

            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    const int Count = 3;
                    System.Console.WriteLine(Count);
                }
            }
            """
        );

        var sample = output["sample.ts"];
        await Assert.That(sample).Contains("const base = 1;");
        await Assert.That(sample).Contains("this._seed = base * 2;");
        await Assert.That(sample).Contains("const rounding = 1;");
        await Assert.That(sample).Contains("const step = 2;");
        await Assert.That(sample).Contains("return this.total + step;");

        var program = output["program.ts"];
        await Assert.That(program).Contains("const count = 3;");
        await Assert.That(program).Contains("console.log(count)");
    }

    [Test]
    public async Task ConstLocalNamedLikeReservedWord_EscapesAtDeclarationAndReads()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static string F(string x)
                {
                    const string Default = "d";
                    const string New = "n";
                    return Default + New + x;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const default_ = \"d\";");
        await Assert.That(output).Contains("const new_ = \"n\";");
        await Assert.That(output).Contains("return default_ + new_ + x;");
    }

    [Test]
    public async Task NonConstLocal_PascalCaseName_MatchesCamelCaseDeclaration()
    {
        // The declaration/read agreement is not const-specific: any local whose
        // C# name is not camelCase renames on both halves.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int x)
                {
                    int Total = x * 2;
                    Total += 1;
                    return Total;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("let total = x * 2;");
        await Assert.That(output).Contains("total += 1;");
        await Assert.That(output).Contains("return total;");
        await Assert.That(output).DoesNotContain("Total");
    }

    [Test]
    public async Task ModuleConst_KeepsCSharpName_AtDeclarationAndSameFileReads()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Globals
            {
                public const int MaxItems = 3;

                public static int Cap(int x) => x > MaxItems ? MaxItems : x;
            }
            """
        )["globals.ts"];

        await Assert.That(output).Contains("export const MaxItems: number = 3;");
        await Assert.That(output).Contains("return x > MaxItems ? MaxItems : x;");
        await Assert.That(output).DoesNotContain("maxItems");
    }

    [Test]
    public async Task ModuleConst_KeepsCSharpName_InCrossFileReads()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Globals
            {
                public const int MaxItems = 3;
            }

            [Transpile]
            public class Reader
            {
                public int Read() => Globals.MaxItems;
            }
            """
        );

        var reader = output["reader.ts"];
        await Assert.That(reader).Contains("import { MaxItems } from \"./globals\";");
        await Assert.That(reader).Contains("return MaxItems;");
    }

    [Test]
    public async Task ModuleConst_KeepsCSharpName_InCrossPackageReads()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            [ExportedAsModule]
            public static class Limits
            {
                public const int MaxItems = 3;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            public class Reader
            {
                public int Read() => Limits.MaxItems;
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["reader.ts"];

        await Assert.That(output).Contains("import { MaxItems } from \"shared\"");
        await Assert.That(output).Contains("return MaxItems;");
    }

    [Test]
    public async Task ClassConstField_StaysCamelCase_AtDeclarationAndAccess()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public const int Limit = 10;

                public int Double() => Limit * 2;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("static readonly limit: number = 10;");
        await Assert.That(output).Contains("return Sample.limit * 2;");
        await Assert.That(output).DoesNotContain("Limit");
    }

    [Test]
    public async Task NameOverrideOnModuleConst_WinsVerbatim()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Globals
            {
                [Name("MAX_ITEMS")]
                public const int MaxItems = 3;

                public static int Cap(int x) => x > MaxItems ? MaxItems : x;
            }
            """
        )["globals.ts"];

        await Assert.That(output).Contains("export const MAX_ITEMS: number = 3;");
        await Assert.That(output).Contains("return x > MAX_ITEMS ? MAX_ITEMS : x;");
        await Assert.That(output).DoesNotContain("MaxItems");
    }
}
