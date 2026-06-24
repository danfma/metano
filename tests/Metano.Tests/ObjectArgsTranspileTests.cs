namespace Metano.Tests;

public class ObjectArgsTranspileTests
{
    [Test]
    public async Task ModuleFunction_ObjectArgs_EmitsObjectParamSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int gap = 0, int width = 100) => gap + width;
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert
            .That(output)
            .Contains("function column(args: { gap?: number; width?: number }): number");
        await Assert.That(output).Contains("const { gap = 0, width = 100 } = args;");
    }

    [Test]
    public async Task ModuleFunction_ObjectArgs_RequiredParamHasNoQuestionMark()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Layout(int width, int padding = 0) => width + padding;
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert
            .That(output)
            .Contains("function layout(args: { width: number; padding?: number }): number");
    }

    [Test]
    public async Task ModuleFunction_ObjectArgs_FieldTypeFromOtherFileIsImported()
    {
        // The synthesized `args: { ... }` parameter type must keep its
        // nested type references visible to the import collector — the
        // bridge cannot serialize the shape as raw text. Here `Widget`
        // lives in its own file, so `ui.ts` must declare an explicit
        // import for it.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile]
            public abstract class Widget
            {
                public abstract string Render();
            }

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static Widget Container(Widget[] children) => children[0];
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import");
        await Assert.That(output).Contains("Widget");
        await Assert.That(output).Contains("children: Widget[]");
    }

    [Test]
    public async Task CallSite_ObjectArgs_PositionalAndNamedCollapseToObjectLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int width, int gap = 0) => width + gap;
            }

            [Transpile]
            public class Widget
            {
                public int A() => UI.Column(width: 100, gap: 12);
                public int B() => UI.Column(50, 4);
                public int C() => UI.Column(width: 200);
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("column({ width: 100, gap: 12 })");
        await Assert.That(output).Contains("column({ width: 50, gap: 4 })");
        await Assert.That(output).Contains("column({ width: 200 })");
    }

    [Test]
    public async Task InstanceMethod_ObjectArgs_EmitsObjectParamSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public int Make(int gap = 0, int width = 100) => gap + width;
            }
            """
        );

        var output = result["app/renderer.ts"];
        await Assert.That(output).Contains("make(args: { gap?: number; width?: number }): number");
        await Assert.That(output).Contains("const { gap = 0, width = 100 } = args;");
    }

    [Test]
    public async Task InstanceMethod_ObjectArgs_RequiredParamHasNoQuestionMark()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public int Layout(int width, int padding = 0) => width + padding;
            }
            """
        );

        var output = result["app/renderer.ts"];
        await Assert
            .That(output)
            .Contains("layout(args: { width: number; padding?: number }): number");
    }

    [Test]
    public async Task InstanceMethod_ObjectArgs_CallSitesCollapseToObjectLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public int Make(int width, int gap = 0) => width + gap;
            }

            [Transpile]
            public class Caller
            {
                public int A(Renderer r) => r.Make(width: 100, gap: 12);
                public int B(Renderer r) => r.Make(50, 4);
                public int C(Renderer r) => r.Make(width: 200);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("r.make({ width: 100, gap: 12 })");
        await Assert.That(output).Contains("r.make({ width: 50, gap: 4 })");
        await Assert.That(output).Contains("r.make({ width: 200 })");
    }

    [Test]
    public async Task InstanceMethod_NoObjectArgs_KeepsPositionalSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                public int Plain(int gap = 0, int width = 100) => gap + width;
            }
            """
        );

        var output = result["app/renderer.ts"];
        await Assert.That(output).Contains("plain(gap: number = 0, width: number = 100): number");
        await Assert.That(output).DoesNotContain("plain(args:");
    }

    [Test]
    public async Task StaticMethod_ObjectArgs_EmitsObjectParamSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public static int Make(int gap = 0, int width = 100) => gap + width;
            }
            """
        );

        var output = result["app/renderer.ts"];
        await Assert
            .That(output)
            .Contains("static make(args: { gap?: number; width?: number }): number");
        await Assert.That(output).Contains("const { gap = 0, width = 100 } = args;");
    }

    [Test]
    public async Task StaticMethod_ObjectArgs_CallSiteCollapsesToObjectLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public static int Make(int gap = 0, int width = 100) => gap + width;
            }

            [Transpile]
            public class Caller
            {
                public int A() => Renderer.Make(gap: 12, width: 200);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("Renderer.make({ gap: 12, width: 200 })");
    }

    [Test]
    public async Task ModuleFunction_NoObjectArgs_KeepsPositionalSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                public static int Plain(int gap = 0, int width = 100) => gap + width;
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert
            .That(output)
            .Contains("function plain(gap: number = 0, width: number = 100): number");
        await Assert.That(output).DoesNotContain("function plain(args:");
    }

    [Test]
    public async Task Constructor_ObjectArgs_EmitsPositionalCtorAndCreateFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Column
            {
                [ObjectArgs]
                public Column(int gap = 0, int width = 100)
                {
                    Gap = gap;
                    Width = width;
                }

                public int Gap { get; }
                public int Width { get; }
            }
            """
        );

        var output = result["app/column.ts"];
        await Assert
            .That(output)
            .Contains("constructor(readonly gap: number = 0, readonly width: number = 100)");
        await Assert
            .That(output)
            .Contains("static create(args: { gap?: number; width?: number }): Column");
        await Assert.That(output).Contains("const { gap = 0, width = 100 } = args;");
        await Assert.That(output).Contains("return new Column(gap, width);");
    }

    [Test]
    public async Task Constructor_ObjectArgs_CallSiteCollapsesToCreateFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Column
            {
                [ObjectArgs]
                public Column(int gap = 0, int width = 100)
                {
                    Gap = gap;
                    Width = width;
                }

                public int Gap { get; }
                public int Width { get; }
            }

            [Transpile]
            public class Caller
            {
                public Column A() => new Column(gap: 12, width: 200);
                public Column B() => new Column(50);
                public Column C() => new Column();
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("Column.create({ gap: 12, width: 200 })");
        await Assert.That(output).Contains("Column.create({ gap: 50 })");
        await Assert.That(output).Contains("Column.create({})");
        await Assert.That(output).DoesNotContain("new Column(gap");
        await Assert.That(output).DoesNotContain("new Column(50");
    }

    [Test]
    public async Task ObjectArgs_ParamsTrailingArgs_FoldIntoArrayLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int gap, params int[] children) => gap + children.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Make() => UI.Column(gap: 12, 1, 2, 3);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("gap: 12,");
        await Assert.That(output).Contains("children: [1, 2, 3]");
    }

    [Test]
    public async Task ObjectArgs_ParamsExplicitArray_KeepsArray()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int gap, params int[] children) => gap + children.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Make() => UI.Column(gap: 12, children: new[] { 1, 2, 3 });
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("gap: 12");
        await Assert.That(output).Contains("children: [1, 2, 3]");
    }

    [Test]
    public async Task ObjectArgs_ParamsEmpty_OmitsChildrenField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static int Column(int gap, params int[] children) => gap + children.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Make() => UI.Column(gap: 12);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("column({ gap: 12 })");
    }

    [Test]
    public async Task Constructor_ObjectArgs_PreservesTypeArgumentsAtCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Box<T>
            {
                [ObjectArgs]
                public Box(T value) { Value = value; }
                public T Value { get; }
            }

            [Transpile]
            public class Caller
            {
                public Box<int> MakeInt() => new Box<int>(value: 12);
                public Box<string> MakeString() => new Box<string>(value: "hello");
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("Box.create<number>({ value: 12 })");
        await Assert.That(output).Contains("Box.create<string>({ value: \"hello\" })");
    }

    [Test]
    public async Task Constructor_ObjectArgs_HonorsNameOverrideOnCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, Name(TargetLanguage.TypeScript, "MyColumn")]
            public class Column
            {
                [ObjectArgs]
                public Column(int gap = 0, int width = 100)
                {
                    Gap = gap;
                    Width = width;
                }

                public int Gap { get; }
                public int Width { get; }
            }

            [Transpile]
            public class Caller
            {
                public Column Make() => new Column(gap: 12, width: 200);
            }
            """
        );

        await Assert.That(result.Keys).Contains("app/my-column.ts");
        var emitted = result["app/my-column.ts"];
        await Assert
            .That(emitted)
            .Contains("static create(args: { gap?: number; width?: number }): MyColumn");
        await Assert.That(emitted).Contains("return new MyColumn(gap, width);");

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("MyColumn.create({ gap: 12, width: 200 })");
    }

    [Test]
    public async Task Constructor_NoObjectArgs_KeepsPositionalNew()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Plain
            {
                public Plain(int a, int b)
                {
                    A = a;
                    B = b;
                }

                public int A { get; }
                public int B { get; }
            }

            [Transpile]
            public class Caller
            {
                public Plain Make() => new Plain(1, 2);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("new Plain(1, 2)");
        await Assert.That(output).DoesNotContain(".create(");
    }

    [Test]
    public async Task Constructor_ObjectArgs_WithMultipleCtors_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public class Column
            {
                [ObjectArgs]
                public Column(int gap)
                {
                    Gap = gap;
                }

                public Column(int gap, int width)
                {
                    Gap = gap;
                    Width = width;
                }

                public int Gap { get; }
                public int Width { get; }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("[ObjectArgs]")
                    && d.Message.Contains("overloading")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task ObjectArgs_OnOverloadedMethod_EmitsDiagnosticAndSkips()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public class Renderer
            {
                [ObjectArgs]
                public int Make(int gap) => gap;
                public int Make(int gap, int width) => gap + width;
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("[ObjectArgs]")
                    && d.Message.Contains("overloading")
                )
            )
            .IsTrue();
    }
}
