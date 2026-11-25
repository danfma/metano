namespace Metano.Tests;

/// <summary>
/// <c>[ObjectArgs]</c> on a primary constructor (<c>[method: ObjectArgs]</c>) and
/// type-level on a one-constructor type (#739): the positional runtime constructor
/// stays, the <c>create({...})</c> factory takes the object, call sites collapse to it.
/// </summary>
public class ObjectArgsPrimaryConstructorTests
{
    [Test]
    public async Task MethodTargetOnPositionalRecord_EmitsFactoryAndKeepsValueSemantics()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);
            """
        );

        var output = result["app/point.ts"];

        await Assert.That(output).Contains("constructor(x: number, y: number = 2) {");

        await Assert
            .That(output)
            .Contains(
                """
                  static create(args: { x: number; y?: number }): Point {
                    const { x, y = 2 } = args;

                    return new Point(x, y);
                  }
                """
            );

        await Assert.That(output).Contains("equals(other: any): boolean {");
        await Assert.That(output).Contains("hashCode(): number {");
        await Assert.That(output).Contains("copyRecordWith<Point>(this, overrides)");
        await Assert.That(output).Contains("toString(): string {");
    }

    [Test]
    public async Task TypeLevelOnPositionalRecord_EmitsFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public record Entry(int A, string B = "x");
            """
        );

        var output = result["app/entry.ts"];

        await Assert.That(output).Contains("constructor(a: number, b: string = \"x\") {");

        await Assert
            .That(output)
            .Contains("static create(args: { a: number; b?: string }): Entry {");

        await Assert.That(output).Contains("const { a, b = \"x\" } = args;");
    }

    [Test]
    public async Task TypeLevelOnPrimaryConstructorClass_EmitsFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public class Cls(int a, int b = 5)
            {
                public int Sum => a + b;
            }
            """
        );

        var output = result["app/cls.ts"];

        await Assert.That(output).Contains("static create(args: { a: number; b?: number }): Cls {");
        await Assert.That(output).Contains("return new Cls(a, b);");
    }

    [Test]
    public async Task CallSites_PositionalNamedAndDefaulted_CollapseToCreate()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [Transpile, ObjectArgs]
            public record Entry(int A, string B = "x");

            [Transpile]
            public class Use
            {
                public Point Omitted() => new Point(1);
                public Point Both() => new Point(1, 7);
                public Entry Named() => new Entry(B: "y", A: 1);
                public Point Copy(Point p) => p with { X = 3 };
            }
            """
        );

        var output = result["app/use.ts"];

        await Assert.That(output).Contains("return Point.create({ x: 1 });");
        await Assert.That(output).Contains("return Point.create({ x: 1, y: 7 });");
        await Assert.That(output).Contains("return Entry.create({ b: \"y\", a: 1 });");
        await Assert.That(output).Contains("return p.with({ x: 3 });");
        await Assert.That(output).DoesNotContain("new Point(");
        await Assert.That(output).DoesNotContain("=> ");
    }

    [Test]
    public async Task DerivedRecord_PassesPositionalArgsToObjectArgsBase()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [Transpile]
            [method: ObjectArgs]
            public record Point3(int X, int Y = 2, int Z = 0) : Point(X, Y);

            [Transpile]
            public class Use
            {
                public Point3 Make() => new Point3(1, 2, 3);
            }
            """
        );

        var derived = result["app/point3.ts"];

        await Assert
            .That(derived)
            .Contains("constructor(x: number, y: number = 2, z: number = 0) {");

        await Assert.That(derived).Contains("super(x, y);");

        await Assert
            .That(derived)
            .Contains(
                "static override create(args: { x: number; y?: number; z?: number }): Point3 {"
            );

        await Assert.That(result["app/use.ts"]).Contains("Point3.create({ x: 1, z: 3 })");
    }

    [Test]
    public async Task CrossPackageConsumer_SeesTheSameCreateSurface()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace MyLib;

            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [ObjectArgs]
            public record Entry(int A, string B = "x");
            """;
        var consumer = """
            using MyLib;

            namespace App;

            [Transpile]
            public class Use
            {
                public Point A() => new Point(1);
                public Entry B() => new Entry(B: "y", A: 1);
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/use.ts"];

        await Assert.That(output).Contains("return Point.create({ x: 1 });");
        await Assert.That(output).Contains("return Entry.create({ b: \"y\", a: 1 });");
    }

    [Test]
    public async Task TypeLevelOnTypeWithSeveralConstructors_ReportsInsteadOfIgnoring()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public class Column
            {
                public Column(int gap) { Gap = gap; }
                public Column(int gap, int width) { Gap = gap; }
                public int Gap { get; }
            }

            [Transpile]
            public class Use
            {
                public Column Make() => new Column(1);
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("[ObjectArgs] on type 'Column'")
                )
            )
            .IsTrue();

        await Assert.That(files["app/column.ts"]).DoesNotContain("static create(");
        await Assert.That(files["app/use.ts"]).DoesNotContain("Column.create");
    }

    [Test]
    public async Task NamedArgumentsOutOfOrder_KeepEvaluationOrderInTheLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [Transpile]
            public class Use
            {
                static int L(int v) => v;

                public Point Make() => new Point(Y: L(2), X: L(1));
            }
            """
        );

        await Assert
            .That(result["app/use.ts"])
            .Contains("return Point.create({ y: Use.l(2), x: Use.l(1) });");
    }

    [Test]
    public async Task ParamsParameter_DefaultsToEmptyAndSpreadsIntoTheConstructor()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Tags(string Name, params string[] Items);

            [Transpile]
            public class Use
            {
                public Tags A() => new Tags("a", "x", "y");
                public Tags B() => new Tags("b");
                public Tags C() => new Tags("c", new[] { "z" });
            }
            """
        );

        var tags = result["app/tags.ts"];
        var use = Flat(result["app/use.ts"]);

        var factory = "static create(args: { name: string; items?: string[] }): Tags {";
        var withItems = "return Tags.create({ name: \"a\", items: [\"x\", \"y\"], });";

        await Assert.That(tags).Contains(factory);
        await Assert.That(tags).Contains("const { name, items = [] } = args;");
        await Assert.That(tags).Contains("return new Tags(name, ...items);");
        await Assert.That(use).Contains(withItems);
        await Assert.That(use).Contains("return Tags.create({ name: \"b\" });");
        await Assert.That(use).Contains("return Tags.create({ name: \"c\", items: [\"z\"], });");
    }

    [Test]
    public async Task AbstractType_HasNoFactoryAndReports()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public abstract record Shape(string Name);

            [Transpile]
            public record Circle(double R) : Shape("circle");
            """
        );

        await Assert.That(files["app/shape.ts"]).DoesNotContain("static create(");
        await Assert.That(HasUnsupported(diagnostics, "abstract type 'Shape'")).IsTrue();
    }

    [Test]
    public async Task PlainObject_KeepsObjectLiteralAndReports()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, PlainObject, ObjectArgs]
            public record Dto(int A, string? B = null);

            [Transpile]
            public class Use
            {
                public Dto Make() => new Dto(1);
            }
            """
        );

        await Assert.That(files["app/use.ts"]).DoesNotContain("Dto.create");
        await Assert.That(files["app/use.ts"]).Contains("a: 1");
        await Assert.That(HasUnsupported(diagnostics, "[PlainObject] type 'Dto'")).IsTrue();
    }

    [Test]
    public async Task Dart_KeepsPositionalConstruction()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [Transpile]
            public class Use
            {
                public Point Make() => new Point(1);
            }
            """
        );

        await Assert.That(string.Join("\n", files.Values)).DoesNotContain("TODO: IrObjectLiteral");
    }

    [Test]
    public async Task DerivedFactory_OverridesOnlyWhenTheBaseKeysStayCompatible()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Point(int X, int Y = 2);

            [Transpile]
            [method: ObjectArgs]
            public record Narrower(int X, int Y) : Point(X, Y);

            [Transpile]
            [method: ObjectArgs]
            public record Box<T>(T Value, int Count = 1);

            [Transpile]
            [method: ObjectArgs]
            public record IntBox(int Value) : Box<int>(Value);

            [Transpile]
            public class Use
            {
                public Narrower A() => new Narrower(1, 2);
                public IntBox B() => new IntBox(1);
            }
            """
        );

        await Assert.That(files["app/narrower.ts"]).DoesNotContain("static override create(");
        await Assert.That(files["app/narrower.ts"]).DoesNotContain("static create(");
        await Assert.That(files["app/int-box.ts"]).DoesNotContain("create(");
        await Assert.That(files["app/use.ts"]).Contains("new Narrower(1, 2)");
        await Assert.That(files["app/use.ts"]).Contains("new IntBox(1)");
        await Assert.That(HasUnsupported(diagnostics, "cannot override")).IsTrue();
    }

    [Test]
    public async Task UserStaticCreate_ReportsInsteadOfDuplicatingTheFactory()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Coll(int A)
            {
                public static int Create() => 1;
            }
            """
        );

        await Assert.That(files["app/coll.ts"]).DoesNotContain("static create(args");
        await Assert.That(HasUnsupported(diagnostics, "static member named 'create'")).IsTrue();
    }

    [Test]
    public async Task TypeLevelOnStaticClass_SaysThereIsNoConstructor()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public static class Helpers
            {
                public static int F(int a) => a;
            }
            """
        );

        await Assert.That(HasUnsupported(diagnostics, "no constructor to lower")).IsTrue();
    }

    [Test]
    public async Task NullParamsArgument_OmitsTheProperty()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            public record Tg(string Name, params string[] Items);

            [Transpile]
            public class Use
            {
                public Tg Make() => new Tg("a", null!);
            }
            """
        );

        var use = Flat(result["app/use.ts"]);

        await Assert.That(use).Contains("return Tg.create({ name: \"a\" });");
        await Assert.That(use).DoesNotContain("items");
    }

    [Test]
    public async Task InheritedStaticCreate_ReportsOnTheObjectArgsType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            public class SB
            {
                public static SB Create() => new();
            }

            [Transpile, ObjectArgs]
            public class D8(int a) : SB
            {
                public int A => a;
            }
            """
        );

        await Assert.That(files["app/d8.ts"]).DoesNotContain("static create(args");
        await Assert.That(HasUnsupported(diagnostics, "static member named 'create'")).IsTrue();
    }

    [Test]
    public async Task DerivedStaticCreateUnderObjectArgsBase_Reports()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, ObjectArgs]
            public class Base(int a)
            {
                public int A => a;
            }

            [Transpile]
            public class Derived(int a) : Base(a)
            {
                public static Derived Create(string s) => new(s.Length);
            }
            """
        );

        await Assert.That(HasUnsupported(diagnostics, "inherits from 'Base'")).IsTrue();
    }

    [Test]
    [Arguments(
        "public record Base(int X, int Y = 2);",
        "public record D(int X) : Base(X)",
        "missing"
    )]
    [Arguments("public class Base(int x);", "public class D(long x) : Base((int)x)", "differs")]
    [Arguments("public record Base(string? S);", "public record D(string S) : Base(S)", "differs")]
    [Arguments("public record Base(int X = 1);", "public record D(int X) : Base(X)", "differs")]
    public async Task DerivedOverride_IncompatibleVariants_Report(
        string baseRecord,
        string derivedRecord,
        string reason
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            namespace App;

            [Transpile]
            [method: ObjectArgs]
            {{baseRecord}}

            [Transpile]
            [method: ObjectArgs]
            {{derivedRecord}};
            """
        );

        await Assert.That(HasUnsupported(diagnostics, "cannot override")).IsTrue();
        await Assert.That(HasUnsupported(diagnostics, reason)).IsTrue();
        await Assert.That(files["app/d.ts"]).DoesNotContain("create(");
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    private static bool HasUnsupported(
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> diagnostics,
        string text
    ) =>
        diagnostics.Any(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
            && d.Message.Contains(text)
        );
}
