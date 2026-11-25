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
}
