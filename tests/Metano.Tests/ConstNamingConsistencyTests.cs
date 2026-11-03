using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Naming policy for <c>const</c> bindings (#390): every local binding — a
/// local (a <c>const</c> local included), a parameter, a foreach or catch
/// variable, a pattern designator, an out-var — is camelCased with JS
/// reserved-word escaping at its declaration and at every read; a
/// module-scope compile-time constant (a C# <c>const</c> field of a
/// containerless static class the toolchain declares) keeps its C# name
/// verbatim at the declaration and at every read (same file, cross-file and
/// cross-package); a <c>const</c> field of a regular class is a data member
/// and stays camelCase; a <c>[Name]</c> override always wins verbatim.
/// Whatever the rule, the declaration and every reference must resolve to the
/// same emitted name.
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
        await Assert.That(output).DoesNotContain("const K =");
        await Assert.That(output).DoesNotContain("return K");
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
        await Assert.That(output).DoesNotContain("const Factor =");
        await Assert.That(output).DoesNotContain("return Factor");
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
        await Assert.That(output).DoesNotContain("const Bonus =");
        await Assert.That(output).DoesNotContain("return Bonus");
    }

    [Test]
    public async Task ConstLocalInsideConstructor_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private readonly int _seed;

                public Sample()
                {
                    const int Base = 1;
                    _seed = Base * 2;
                }

                public int Seed => _seed;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const base = 1;");
        await Assert.That(output).Contains("this._seed = base * 2;");
        await Assert.That(output).DoesNotContain("const Base =");
        await Assert.That(output).DoesNotContain("= Base");
    }

    [Test]
    public async Task ConstLocalInsideAccessor_MatchesCamelCaseDeclarationAndRead()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private int _total;

                public int Total
                {
                    get
                    {
                        const int Rounding = 1;
                        return _total + Rounding;
                    }
                    set { _total = value; }
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const rounding = 1;");
        await Assert.That(output).Contains("return this._total + rounding;");
        await Assert.That(output).DoesNotContain("const Rounding =");
        await Assert.That(output).DoesNotContain("+ Rounding");
    }

    [Test]
    public async Task ConstLocalInsideModuleEntryPoint_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
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
        )["program.ts"];

        await Assert.That(output).Contains("const count = 3;");
        await Assert.That(output).Contains("console.log(count)");
        await Assert.That(output).DoesNotContain("const Count =");
        await Assert.That(output).DoesNotContain("log(Count)");
    }

    [Test]
    public async Task LocalNamedLikeReservedWord_EscapesWhenAlreadyLowerCase()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F()
                {
                    var @default = 2;
                    var @new = @default + 1;
                    return @new;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const default_ = 2;");
        await Assert.That(output).Contains("const new_ = default_ + 1;");
        await Assert.That(output).Contains("return new_;");
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
        await Assert.That(output).DoesNotContain("let Total =");
        await Assert.That(output).DoesNotContain("return Total");
    }

    [Test]
    public async Task PascalCaseForEachVariable_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int[] xs)
                {
                    var sum = 0;
                    foreach (var Item in xs)
                    {
                        sum += Item;
                    }
                    return sum;
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("for (const item of xs)");
        await Assert.That(output).Contains("sum += item;");
        await Assert.That(output).DoesNotContain("of Item");
        await Assert.That(output).DoesNotContain("+ Item");
    }

    [Test]
    public async Task PascalCaseCatchVariable_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static string F()
                {
                    try
                    {
                        return Risky();
                    }
                    catch (System.Exception Ex)
                    {
                        return Ex.Message;
                    }
                }

                private static string Risky() => throw new System.Exception("boom");
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("catch (ex)");
        await Assert.That(output).Contains("return ex.message;");
        await Assert.That(output).DoesNotContain("(Ex)");
        await Assert.That(output).DoesNotContain("Ex.message");
    }

    [Test]
    public async Task PascalCaseIsPatternDesignator_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static string F(object o)
                {
                    if (o is int Num)
                    {
                        return (Num + 1).ToString();
                    }
                    return "no";
                }
            }
            """
        )["sample.ts"];

        // The declaration is the typed `let` extraction hoists; the test
        // assigns and the body reads the same emitted name.
        await Assert.That(output).Contains("let num:");
        await Assert.That(output).Contains("num = o");
        await Assert.That(output).Contains("(num + 1)");
    }

    [Test]
    public async Task PascalCaseListPatternDesignators_MatchCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int[] xs)
                {
                    if (xs is [var First, .. var Rest])
                    {
                        return First + Rest.Length;
                    }
                    return 0;
                }
            }
            """
        )["sample.ts"];

        // The hoisted `let`s, the in-test assignments and the body reads all
        // name the same emitted identifiers.
        await Assert.That(output).Contains("let first: number;");
        await Assert.That(output).Contains("let rest: number[] | null;");
        await Assert.That(output).Contains("(first = xs[0]!, true)");
        await Assert.That(output).Contains("(rest = xs.slice(1), true)");
        await Assert.That(output).Contains("return first + rest.length;");
        await Assert.That(output).DoesNotContain("First");
        await Assert.That(output).DoesNotContain("Rest");
    }

    [Test]
    public async Task PascalCaseSwitchExpressionDesignator_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(object o) =>
                    o switch
                    {
                        int N when N > 0 => N,
                        string S => S.Length,
                        _ => 0,
                    };
            }
            """
        )["sample.ts"];

        // The arm binds the designator (`const n = $s;`) and both the `when`
        // guard and the outcome read the emitted name.
        await Assert.That(output).Contains("const n =");
        await Assert.That(output).Contains("n > 0");
        await Assert.That(output).Contains("const s =");
        await Assert.That(output).Contains("return s.length;");
    }

    [Test]
    public async Task PascalCaseSwitchStatementCaseDesignator_MatchesCamelCaseDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(object o)
                {
                    switch (o)
                    {
                        case int N:
                            return N * 2;
                        default:
                            return 0;
                    }
                }
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("const n =");
        await Assert.That(output).Contains("return n * 2;");
        await Assert.That(output).DoesNotContain("return N");
    }

    [Test]
    public async Task PascalCaseOutVar_MatchesDestructuredDeclaration()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private readonly System.Collections.Generic.Dictionary<string, int> _counts = new()
                {
                    { "a", 1 },
                };

                public int Fetch(string key) =>
                    _counts.TryGetValue(key, out var Total) ? Total : 0;
            }
            """
        )["sample.ts"];

        // The destructured binding and the read of it name the same local.
        await Assert.That(output).Contains("? total : 0");
        await Assert.That(output).DoesNotContain("Total");
    }

    [Test]
    public async Task ExportVarFromBody_PascalCaseLocal_ExportsInPlaceUnderEmittedName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("App", InPlace = true)]
                public static void Main()
                {
                    var App = 1 + 2;
                    System.Console.WriteLine(App);
                }
            }
            """
        );

        var program = files["program.ts"];
        await Assert.That(program).Contains("export const app = 1 + 2;");
        await Assert.That(program).Contains("console.log(app)");

        await Assert
            .That(diagnostics.Any(d => d.Message.Contains("could not export the local variable")))
            .IsFalse();
    }

    [Test]
    public async Task ExportVarFromBody_PascalCaseLocal_ExportsTrailingUnderEmittedName()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("App", AsDefault = true)]
                public static void Main()
                {
                    var App = new System.Text.StringBuilder();
                }
            }
            """
        )["program.ts"];

        await Assert.That(output).Contains("const app = ");
        await Assert.That(output).Contains("export default app;");
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
    public async Task ModuleStaticReadOnly_SiblingOfConst_StaysCamelCaseWhileConstKeepsName()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Limits
            {
                public static readonly int Fallback = 10;
                public const int HardCap = 20;

                public static int Pick(bool hard) => hard ? HardCap : Fallback;
            }
            """
        )["limits.ts"];

        await Assert.That(output).Contains("export const fallback: number = 10;");
        await Assert.That(output).Contains("export const HardCap: number = 20;");
        await Assert.That(output).Contains("return hard ? HardCap : fallback;");
    }

    [Test]
    public async Task ModuleConstNamedLikeReservedWord_Escapes()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Globals
            {
                public const int @new = 9;

                public static int F(int x) => @new + x;
            }
            """
        )["globals.ts"];

        await Assert.That(output).Contains("export const new_: number = 9;");
        await Assert.That(output).Contains("return new_ + x;");
    }

    [Test]
    public async Task ModuleConstNextToSameSpellingFunction_ReportsNoCollision()
    {
        // `const Cap` keeps its C# name (`Cap`) while `cap()` emits `cap`, so
        // the module declares two distinct names — no MS0001. Under the old
        // camelCase rule both emitted `cap` and the check fired.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Counter
            {
                public const int Cap = 1;

                public static int cap() => Cap;
            }
            """
        );

        await Assert.That(files).ContainsKey("counter.ts");
        await Assert.That(files["counter.ts"]).Contains("export const Cap: number = 1;");

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Error
                    && d.Message.Contains("emitted under the same name")
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task ModuleConstReadNextToSameSpellingLocal_ReportsNoShadow()
    {
        // The read emits `Max`; the local `max` emits `max` — distinct TS
        // identifiers, so the shadow diagnostic must not fire.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public const int Max = 10;

                public static int F()
                {
                    var max = 3;
                    return Max + max;
                }
            }
            """
        );

        await Assert.That(files).ContainsKey("limits.ts");
        await Assert.That(files["limits.ts"]).Contains("export const Max: number = 10;");
        await Assert.That(files["limits.ts"]).Contains("const max = 3;");
        await Assert.That(files["limits.ts"]).Contains("return Max + max;");
        await Assert.That(diagnostics.Any(d => d.Message.Contains("shadows"))).IsFalse();
    }

    [Test]
    public async Task ModuleStaticReadOnlyNamedLikeReservedWord_EscapesAtDeclarationAndReads()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Globals
            {
                public static readonly int @new = 9;

                public static int F(int x) => @new + x;
            }

            [Transpile]
            public class Reader
            {
                public int Read() => Globals.@new;
            }
            """
        );

        var globals = output["globals.ts"];
        await Assert.That(globals).Contains("export const new_: number = 9;");
        await Assert.That(globals).Contains("return new_ + x;");

        var reader = output["reader.ts"];
        await Assert.That(reader).Contains("import { new_ } from \"./globals\";");
        await Assert.That(reader).Contains("return new_;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtConstructorDeclarationAndRead()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                private readonly int _v;

                public Sample(int of)
                {
                    _v = of + 1;
                }

                public int Value => _v;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("constructor(of_: number)");
        await Assert.That(output).Contains("this._v = of_ + 1;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtLambdaDeclarationAndRead()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static Func<int, int> F() => of => of + 3;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("(of_: number) => of_ + 3");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtOverloadImplAndDispatcher()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public static int F(int of) => of + 1;

                public static int F(int of, int by) => of + by;
            }
            """
        )["sample.ts"];

        await Assert.That(output).Contains("static f(of_: number, by: number): number;");
        await Assert.That(output).Contains("static f(of_: number): number;");
        await Assert
            .That(output)
            .Contains("private static fOfBy(of_: number, by: number): number {");
        await Assert.That(output).Contains("private static fOf(of_: number): number {");
        await Assert.That(output).Contains("return of_ + 1;");
        await Assert.That(output).Contains("return of_ + by;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtModuleFunctionDeclarationAndRead()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Math
            {
                public static int Twice(int of) => of * 2;
            }
            """
        )["math.ts"];

        await Assert.That(output).Contains("export function twice(of_: number): number");
        await Assert.That(output).Contains("return of_ * 2;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_ObjectArgsFunctionKeepsWireKeyAndAliasesBinding()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int of = 0, int width = 100) => of + width;
            }
            """
        )["ui.ts"];

        // The wire key stays `of`; the local binding the body reads is aliased.
        await Assert
            .That(output)
            .Contains("function column(args: { of?: number; width?: number })");
        await Assert.That(output).Contains("const { of: of_ = 0, width = 100 } = args;");
        await Assert.That(output).Contains("return of_ + width;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_ObjectArgsConstructorAliasesBindingInDestructure()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Panel
            {
                [ObjectArgs]
                public Panel(int of, int width)
                {
                    Gap = of;
                    Width = width;
                }

                public int Gap { get; }
                public int Width { get; }
            }
            """
        )["panel.ts"];

        await Assert.That(output).Contains("const { of: of_, width } = args;");
        await Assert.That(output).Contains("this.gap = of_;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtPlainObjectMethodDeclarationAndRead()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Line
            {
                public int Shift(int of) => of + 1;
            }
            """
        )["line.ts"];

        await Assert
            .That(output)
            .Contains("export function shift(self: Line, of_: number): number {");
        await Assert.That(output).Contains("  return of_ + 1;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtExplicitConstructorParameter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Range
            {
                public Range(int Of)
                {
                    Length = Of;
                }

                public int Length { get; }
            }
            """
        )["range.ts"];

        await Assert.That(output).Contains("constructor(of_: number) {");
        await Assert.That(output).Contains("this.length = of_;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtPrimaryConstructorCapture()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Range(int of)
            {
                private readonly int _of = of;

                public int Length => _of;
            }
            """
        )["range.ts"];

        // The captured field's declaration and the constructor's capture
        // assignment write the same member, and the parameter binding escapes.
        await Assert.That(output).Contains("private readonly _of: number;");
        await Assert.That(output).Contains("constructor(of_: number) {");
        await Assert.That(output).Contains("this._of = of_;");
    }

    [Test]
    public async Task CapturedFieldNamedLikeReservedWord_KeepsOneNameAtDeclarationAndCapture()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Range(int of)
            {
                private readonly int @default = of;

                public int Length => @default;
            }
            """
        )["range.ts"];

        // A reserved word is a valid TS property name, so the data-member rule
        // keeps it; the declaration and the capture assignment agree.
        await Assert.That(output).Contains("private readonly default: number;");
        await Assert.That(output).Contains("this.default = of_;");
    }

    [Test]
    public async Task ParameterNamedLikeReservedWord_EscapesAtConstructorOverloadDispatcher()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Sample
            {
                public Sample(int of, int by)
                {
                    Sum = of + by;
                }

                public Sample(int of)
                    : this(of, 0) { }

                public int Sum { get; }
            }
            """
        )["sample.ts"];

        // Overload signatures, the dispatcher's rebind of args and the
        // `: this(...)` chain binding all escape the parameter name.
        await Assert.That(output).Contains("constructor(of_: number, by: number);");
        await Assert.That(output).Contains("constructor(of_: number);");
        await Assert.That(output).Contains("const of_ = args[0] as number;");
        await Assert.That(output).Contains("const of_ = __chain0Arg0;");
        await Assert.That(output).Contains("this.sum = of_ + by;");
    }

    [Test]
    public async Task ConstOnExternalContainerlessClass_ReadKeepsMemberNaming()
    {
        // `[External]` declares a runtime-provided shape the toolchain does
        // not emit, so its const is not a module constant: the read keeps the
        // ordinary member rule (camelCase) even though the class is
        // containerless and the access flattens to a bare identifier.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations.TypeScript;

            [External, NoContainer]
            public static class NativeLimits
            {
                public const int HardCap = 5;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Reader
            {
                public int Read() => NativeLimits.HardCap;
            }
            """
        );

        var output = result["reader.ts"];
        await Assert.That(output).Contains("return hardCap;");
        await Assert.That(output).DoesNotContain("HardCap");
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
