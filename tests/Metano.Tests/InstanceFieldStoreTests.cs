using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Instance fields emit as type-only declarations, and every constructor path
/// stores each of them once, in declaration order, after <c>super(...)</c>
/// (#795, W10-1): promoted members first, then the C# initializer, default,
/// captured parameter or <c>undefined!</c> of each field. A trailing field
/// every constructor assigns first, in order, keeps only that store
/// (#787 N11). A static class gets a private constructor, and a class that
/// declares none and stores nothing gets no constructor (#787 N4).
/// </summary>
public class InstanceFieldStoreTests
{
    [Test]
    public async Task Fields_AreDeclaredAndStoredInDeclarationOrderAfterSuper()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base { public Base(int x) { } }

            [Transpile]
            public class Derived : Base
            {
                public int A = 4;
                public string? B;
                public string C;

                public Derived(int x) : base(x) { A = 1; }
            }
            """
        )["derived.ts"];

        await Assert.That(output).Contains("  declare a: number;\n  declare b: string | null;\n");
        await Assert.That(output).Contains("  declare c: string;\n");

        await Assert
            .That(output)
            .Contains(
                "    super(x);\n"
                    + "    this.a = 4;\n"
                    + "    this.b = null;\n"
                    + "    this.c = undefined!;\n"
                    + "    this.a = 1;\n"
            );
    }

    [Test]
    public async Task TrailingFieldsAssignedFirstInOrder_KeepOnlyTheBodyStore()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Holder
            {
                private int _count = 3;
                private System.Func<int>? _compute;
                private int _version;

                public Holder(System.Func<int> compute) { _compute = compute; _version = 2; }
            }
            """
        )["holder.ts"];

        await Assert
            .That(output)
            .Contains(
                "    this._count = 3;\n    this._compute = compute;\n    this._version = 2;\n  }"
            );
    }

    [Test]
    public async Task FieldAssignedOutOfDeclarationOrder_KeepsItsDefaultStore()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Holder
            {
                private System.Func<int>? _compute;
                private int _version;

                public Holder(System.Func<int> compute) { _version = 2; _compute = compute; }
            }
            """
        )["holder.ts"];

        await Assert.That(output).Contains("    this._compute = null;\n    this._version = 2;\n");
        await Assert.That(output).Contains("    this._compute = compute;\n");
    }

    [Test]
    public async Task FieldWithAnEffectfulInitializer_KeepsItsStore()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Holder
            {
                private readonly System.Collections.Generic.List<int> _items = new();

                public Holder(System.Collections.Generic.List<int> items) { _items = items; }
            }
            """
        )["holder.ts"];

        await Assert.That(output).Contains("    this._items = new List<number>();\n");
        await Assert.That(output).Contains("    this._items = items;\n");
    }

    [Test]
    public async Task ChainedConstructor_KeepsEveryDefaultStore()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Chain
            {
                private int _a;

                public Chain() : this(1) { }

                public Chain(int a) { _a = a; }
            }
            """
        )["chain.ts"];

        await Assert.That(output).Contains("        this._a = 0;\n        this._a = a;\n");
    }

    [Test]
    public async Task PromotedMembers_AreDeclaredAndStoredFirst()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X, int Y) { public string? Tag { get; init; } }
            """
        )["point.ts"];

        await Assert
            .That(output)
            .Contains(
                "  declare readonly x: number;\n"
                    + "  declare readonly y: number;\n"
                    + "  declare readonly tag: string | null;\n\n"
                    + "  constructor(x: number, y: number) {\n"
                    + "    this.x = x;\n"
                    + "    this.y = y;\n"
                    + "    this.tag = null;\n"
                    + "  }"
            );
    }

    [Test]
    public async Task OverrideField_KeepsItsInitializedForm()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class QuotaException : System.Exception
            {
                public int Code = 3;

                public QuotaException(string message) : base(message) { }
            }
            """
        )["quota-exception.ts"];

        await Assert
            .That(output)
            .Contains("  override readonly name: string = \"QuotaException\";\n");

        await Assert.That(output).Contains("    this.code = 3;\n  }");
    }

    [Test]
    public async Task StaticClass_GetsAPrivateConstructor()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Tracking { public static int Count; }
            """
        )["tracking.ts"];

        await Assert.That(output).Contains("  private constructor() { }");
    }

    [Test]
    public async Task ClassWithoutConstructorOrFields_EmitsNoConstructor()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Empty { public int M() => 1; }
            """
        )["empty.ts"];

        await Assert.That(output).DoesNotContain("constructor(");
    }

    [Test]
    public async Task InitializerCallingAFunctionNamedLikeAParameterOrLocal_MatchesTheGolden()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Helpers { public static int Compute() => 7; }

            [Transpile]
            public class Shadow
            {
                private int _x = Helpers.Compute();

                public Shadow(int compute) { Y = compute; }

                public int Y;
            }

            [Transpile]
            public class LocalShadow
            {
                private int _x = Helpers.Compute();

                public LocalShadow() { var compute = 3; Y = compute + _x; }

                public int Y;
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "instance-initializer-name-capture.ts",
            files["shadow.ts"] + files["local-shadow.ts"]
        );
    }

    [Test]
    public async Task InitializerCallingAFunctionNamedLikeAnOutVariable_AliasesTheImport()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Helpers
            {
                public static int Compute() => 7;
                public static int Third() => 11;
            }

            [Transpile]
            public class OutShadow
            {
                private int _x = Helpers.Compute();
                private int _t = Helpers.Third();

                public static bool TryGet(string s, out int v) { v = s.Length; return true; }

                public OutShadow(string s)
                {
                    int.TryParse(s, out var compute);
                    if (TryGet(s, out int third)) Y = compute + third;
                }

                public int Y;
            }
            """
        );

        var output = files["out-shadow.ts"];

        await Assert.That(output).Contains("compute as compute$1");
        await Assert.That(output).Contains("third as third$1");
        await Assert.That(output).Contains("    this._x = compute$1();\n");
        await Assert.That(output).Contains("    this._t = third$1();\n");
    }

    [Test]
    public async Task InitializerReadingATypeNamedLikeAParameter_AliasesTheTypeImport()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, Name("widget")]
            public class Widget { public int V = 3; public static int Make() => 9; public static int Count = 2; }

            [Transpile, Name("shade")]
            public enum Shade { Light, Dark }

            [Transpile]
            public static class Holder { public static object O = new Widget(); }

            [Transpile]
            public class EnumShadow
            {
                public Shade S = Shade.Dark;
                public Shade Zero;
                public int Sum;
                public EnumShadow(int shade) { Sum = shade; }
            }

            [Transpile]
            public class TypeShadow
            {
                public Widget W = new Widget();
                public int M = Widget.Make();
                public int N = Widget.Count;
                public bool B = Holder.O is Widget;
                public int Sum;
                public TypeShadow(int widget) { Sum = widget; }
            }
            """
        );

        var enumShadow = files["enum-shadow.ts"];
        var typeShadow = files["type-shadow.ts"];

        await Assert.That(enumShadow).Contains("import { shade, shade as shade$1 }");
        await Assert.That(enumShadow).Contains("    this.s = shade$1.Dark;\n");
        await Assert.That(enumShadow).Contains("    this.zero = shade$1.Light;\n");
        await Assert.That(typeShadow).Contains("widget as widget$1");
        await Assert.That(typeShadow).Contains("    this.w = new widget$1();\n");
        await Assert.That(typeShadow).Contains("    this.m = widget$1.make();\n");
        await Assert.That(typeShadow).Contains("    this.n = widget$1.count;\n");
        await Assert.That(typeShadow).Contains("instanceof widget$1");
    }

    [Test]
    public async Task OwnLowercaseTypeNamedLikeAParameter_ReadsUnaliasedOutsideInstanceInitializers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, Name("zzCopy")]
            public class ZzCopy
            {
                public static readonly ZzCopy Empty = new ZzCopy(0);
                public static int Seed = 4;
                public int V;
                public int Twice => V * ZzCopy.Seed;
                public static ZzCopy Make() => new ZzCopy(1);
                public ZzCopy(int zzCopy) { V = zzCopy; }
                public ZzCopy(string zzCopy) { V = zzCopy.Length; }
            }
            """
        );

        var output = files["zz-copy.ts"];

        await Assert
            .That(diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsEmpty();

        await Assert.That(output).DoesNotContain("zzCopy$");
    }

    [Test]
    public async Task ClassesSharingAFile_ReadAShadowedImportUnderOneAlias()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Calc { public static int Reckon(int x) => x * 7; }

            [Transpile]
            public class Outer
            {
                public int C = Calc.Reckon(1);
                public int Sum;
                public Outer(int reckon) { Sum = reckon; }

                [Transpile]
                public class Inner
                {
                    public int C = Calc.Reckon(2);
                    public int Sum;
                    public Inner(int reckon) { var Reckon = reckon + 1; Sum = Reckon; }
                }
            }

            [Transpile, EmitInFile("pair")]
            public class PairA
            {
                public int C = Calc.Reckon(1);
                public PairA(int reckon) { }
            }

            [Transpile, EmitInFile("pair")]
            public class PairB
            {
                public int C = Calc.Reckon(2);
                public PairB(int reckon) { var Reckon = reckon; }
            }
            """
        );

        var outer = files["outer.ts"];
        var pair = files["pair.ts"];

        await Assert.That(outer).Contains("import { reckon, reckon as reckon$2 }");
        await Assert.That(outer).Contains("    this.c = reckon$2(1);\n");
        await Assert.That(outer).Contains("    this.c = reckon$2(2);\n");
        await Assert.That(pair).Contains("import { reckon, reckon as reckon$2 }");
        await Assert.That(pair).DoesNotContain("reckon$1(");
    }

    [Test]
    public async Task StructFieldWithoutInitializer_StoresTheStructZeroValue()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Pt { public int X; public int Y; public Pt(int x, int y) { X = x; Y = y; } }

            [Transpile]
            public sealed class StructHolder { public Pt Field; public Pt Auto { get; set; } public Pt Explicit = default; }
            """
        );

        var holder = files["struct-holder.ts"];

        await Assert.That(holder).Contains("    this.field = Pt.$default();\n");
        await Assert.That(holder).Contains("    this.auto = Pt.$default();\n");
        await Assert.That(holder).Contains("    this.explicit = Pt.$default();\n");
        await Assert.That(files["pt.ts"]).Contains("static $default(): Pt");
    }

    [Test]
    public async Task ValueFieldWithoutATargetZeroValue_WarnsUnlessEveryConstructorAssignsIt()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class NoDefaults<T>
            {
                public System.DateTime When;
                public System.TimeSpan Span { get; set; }
                public (int, string) Tup;
                public T Val;
                public System.DateOnly Day;
                public System.DateTime Assigned;
                public System.DateTime Half;
                public required System.DateTime Required;
                public int N;

                public NoDefaults() { Assigned = Stamp; Half = Stamp; }

                public NoDefaults(int n) : this() { N = n; }

                public NoDefaults(string s) { Assigned = Stamp; }

                private static readonly System.DateTime Stamp = new(2020, 1, 1);
            }
            """
        );

        var warned = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Severity == MetanoDiagnosticSeverity.Warning
            )
            .Select(d => d.Location!.SourceTree!.GetText().ToString(d.Location.SourceSpan))
            .ToList();

        await Assert.That(warned).IsEquivalentTo(["When", "Span", "Tup", "Val", "Day", "Half"]);
        await Assert.That(files["no-defaults.ts"]).Contains("    this.when = undefined!;\n");
    }

    [Test]
    public async Task DerivedClass_KeepsTheDefaultStoreOfAFieldTheBodyAssignsFirst()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base { }

            [Transpile]
            public class Derived : Base
            {
                private int _k;

                public Derived() { _k = 1; }
            }
            """
        )["derived.ts"];

        await Assert.That(output).Contains("    super();\n    this._k = 0;\n    this._k = 1;\n");
    }

    [Test]
    public async Task ObjectArgsParameterNamedLikeACalledFunction_KeepsItsKeyAndAliasesTheImport()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Helpers { public static int Compute() => 7; }

            [Transpile, ObjectArgs]
            public class Options
            {
                private int _x = Helpers.Compute();

                public Options(int compute) { Y = compute; }

                public int Y { get; }
            }
            """
        );

        var output = files["options.ts"];

        await Assert.That(output).Contains("compute as compute$1");
        await Assert.That(output).Contains("    this._x = compute$1();\n");
        await Assert.That(output).Contains("static create(args: { compute: number }): Options");
    }

    [Test]
    public async Task ImportedFunctionNamedLikeAParameter_MatchesTheGolden()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Ext
            {
                [Import("extCompute", "ext-lib")]
                public static int ExtCompute() => 0;

                [Import("extDefault", "ext-def", AsDefault = true)]
                public static int ExtDefault() => 0;
            }

            [Transpile]
            public class UsesExt
            {
                private int _x = Ext.ExtCompute();
                private int _y = Ext.ExtDefault();

                public UsesExt(System.Func<int> extCompute, int extDefault) { }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "instance-initializer-import-capture.ts",
            files["uses-ext.ts"]
        );
    }

    [Test]
    public async Task ModuleDeclaredNameAShadowingParameterHides_ReportsAtTheParameter()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer, EmitInFile("same")]
            public static class SameHelpers { public static int Calc() => 9; }

            [Transpile, EmitInFile("same")]
            public class UsesSame
            {
                private int _x = SameHelpers.Calc();

                public UsesSame(int calc) { }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);
        var location = error.Location!;
        var spelled = location.SourceTree!.GetText().ToString(location.SourceSpan);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("reads 'calc', which this module declares");
        await Assert.That(spelled).IsEqualTo("calc");
    }
}
