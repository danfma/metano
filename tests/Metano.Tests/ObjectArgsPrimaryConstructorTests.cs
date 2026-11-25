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

        await Assert
            .That(output)
            .Contains("constructor(readonly x: number, readonly y: number = 2) { }");
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

        await Assert
            .That(output)
            .Contains("constructor(readonly a: number, readonly b: string = \"x\") { }");
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
        await Assert.That(output).Contains("return Entry.create({ a: 1, b: \"y\" });");
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
            public record Point3(int X, int Y, int Z = 0) : Point(X, Y);

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
            .Contains("constructor(x: number, y: number, readonly z: number = 0) {");
        await Assert.That(derived).Contains("super(x, y);");
        await Assert
            .That(derived)
            .Contains(
                "static override create(args: { x: number; y: number; z?: number }): Point3 {"
            );
        await Assert.That(result["app/use.ts"]).Contains("Point3.create({ x: 1, y: 2, z: 3 })");
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
        await Assert.That(output).Contains("return Entry.create({ a: 1, b: \"y\" });");
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
}
