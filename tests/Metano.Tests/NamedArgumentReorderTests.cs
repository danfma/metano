namespace Metano.Tests;

public class NamedArgumentReorderTests
{
    [Test]
    public async Task MethodCall_NamedArgumentsOutOfOrder_ReorderedToDeclarationOrder()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public static class UI
            {
                public static int Column(int[] children, int gap = 0) => gap + children.Length;
            }

            [Transpile]
            public class Widget
            {
                public int Build()
                {
                    return UI.Column(gap: 12, children: new[] { 1, 2, 3 });
                }
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("UI.column([1, 2, 3], 12)");
        await Assert.That(output).DoesNotContain("UI.column(12, [");
    }

    [Test]
    public async Task MethodCall_NamedArgumentSkipsOptional_FillsExplicitDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public static class UI
            {
                public static int Render(int body, int title = 7, int level = 1) =>
                    title + body + level;
            }

            [Transpile]
            public class Widget
            {
                public int Build() => UI.Render(body: 1, level: 3);
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("UI.render(1, 7, 3)");
    }

    [Test]
    public async Task MethodCall_PositionalThenNamed_KeepsPrefix()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public static class UI
            {
                public static int Layout(int width, int height = 0, int padding = 0) =>
                    width + height + padding;
            }

            [Transpile]
            public class Widget
            {
                public int Build() => UI.Layout(100, padding: 8);
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("UI.layout(100, 0, 8)");
    }
}
