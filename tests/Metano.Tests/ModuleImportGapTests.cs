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

        await Assert.That(output).Contains("import { shape } from \"./shape\";");
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
}
