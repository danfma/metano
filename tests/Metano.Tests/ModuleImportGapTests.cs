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
}
