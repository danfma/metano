namespace Metano.Tests;

/// <summary>
/// Gaps in module emission, imports, facades and barrels (wave 6, group H): each test
/// pins one shape that emitted code TypeScript rejected or that bound the wrong value.
/// </summary>
public class ModuleImportGapTests
{
    [Test]
    public async Task NestedCaseOfALowercaseRenamedOwner_ImportsTheOwnerAsAValue()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile, Name("shape")]
            public abstract record Shape
            {
                public sealed record Circle(double R) : Shape;
            }

            [Transpile]
            public class Use
            {
                public bool IsCircle(Shape s) => s is Shape.Circle;
            }
            """
        );

        var output = result["app/use.ts"];

        await Assert.That(output).Contains("import { shape } from \"./shape.js\";");
        await Assert.That(output).Contains("s instanceof shape.Circle");
    }

    [Test]
    public async Task DefaultImportFacade_BindsTheNameItsCallSitesSpell()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Res
            {
                [Import("dflt", "ext-def", AsDefault = true)] public static int BarX(int x) => x;
            }

            [Transpile]
            public class Use { public int Go() => Res.BarX(2); }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("import barX from \"ext-def\";");
        await Assert.That(use).Contains("return barX(2);");
    }

    [Test]
    public async Task ImportFacadeNamedLikeAReservedWord_BindsTheEscapedName()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Res
            {
                [Import("remove", "ext-lib")] public static int Delete(int x) => x;
            }

            [Transpile]
            public class Use
            {
                public int Go() => Res.Delete(1);

                public Func<int, int> Group() => Res.Delete;
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("import { remove as delete_ } from \"ext-lib\";");
        await Assert.That(use).Contains("return delete_(1);");
        await Assert.That(use).Contains("return delete_;");
    }

    [Test]
    public async Task ImportFacadeOnANonStaticOrNestedHost_IsReservedAgainstFastPaths()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record RHost { [Import("zap", "ext-lib")] public static int FooX(int x) => x; }

            [Transpile]
            public static class Outer
            {
                public static class CHost { [Import("zip", "ext-lib")] public static int FooS(int x) => x; }
            }

            [Transpile, NoContainer]
            public static class Neighbor
            {
                public static int Foo(int x) => x + 100;
                public static int Foo(string s) => 100;
            }

            [Transpile]
            public class Use
            {
                public int Go() =>
                    RHost.FooX(1) + Outer.CHost.FooS(3) + Neighbor.Foo(1) + Neighbor.Foo("s");
            }
            """
        );

        var use = files["use.ts"];

        await Assert.That(use).Contains("import { zap as fooX, zip as fooS } from \"ext-lib\";");

        await Assert
            .That(use)
            .Contains("return (fooX(1) + fooS(3) + fooX$(1) + fooS$(\"s\")) | 0;");

        await Assert.That(diagnostics.Count(d => d.Code == "MS0067")).IsEqualTo(2);
    }

    [Test]
    public async Task ContainerlessFunctionCallingAnObjectArgsSibling_IsEmitted()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs] public static int Column(int of = 0, int width = 100) => of + width;
                public static int Use() => Column(of: 3, width: 1);
            }
            """
        );

        await Assert.That(files["ui.ts"]).Contains("return column({ of: 3, width: 1 });");
    }

    [Test]
    public async Task ContainerlessConstants_AreDeclaredBeforeTheVariablesThatReadThem()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public static readonly int Derived = MaxItems + 1;
                public const int MaxItems = 3;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(files["limits.ts"])
            .IsEqualTo(
                "export const MaxItems: number = 3;\n"
                    + "export const derived: number = MaxItems + 1;\n"
            );
    }

    [Test]
    public async Task SameNamedModuleVariablesOfTwoContainerlessClasses_ReportMS0020()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer] public static class Limits { public static readonly int Default = 1; }
            [Transpile, NoContainer] public static class Nc { public static readonly int Default = 2; }
            """
        );

        var clash = diagnostics.Single(d => d.Code == "MS0020");

        await Assert.That(clash.Message).Contains("'Nc.Default' is exported as 'default_'");
    }

    [Test]
    public async Task UsingAliasInTheFileDeclaringTheType_LeavesTheDeclarationUnaliased()
    {
        var files = TranspileHelper.Transpile(
            """
            using T2 = P10.Tag;

            namespace P10
            {
                [Transpile]
                public class Tag
                {
                    public static Tag From(int x) => new Tag();
                    public int V => 1;
                }
            }

            namespace Use
            {
                [Transpile]
                public class U { public int Go() => T2.From(1).V; }
            }
            """
        );

        await Assert.That(files["p10/tag.ts"]).Contains("static from(x: number): Tag {");
        await Assert.That(files["p10/tag.ts"]).Contains("return new Tag();");

        await Assert
            .That(files["use/u.ts"])
            .Contains("import { Tag as T2 } from \"../p10/tag.js\";");

        await Assert.That(files["use/u.ts"]).Contains("return T2.from(1).v;");
    }
}
