namespace Metano.Tests;

/// <summary>
/// A switch expression returned directly lowers to statements, not a
/// self-invoking arrow (#735). Other positions lower to a conditional chain
/// (see <see cref="SwitchExpressionChainTests"/>).
/// </summary>
public class SwitchReturnLoweringTests
{
    private const string Source = """
        using System;

        [Transpile]
        public abstract record Shape;

        [Transpile]
        public sealed record Circle(int Radius) : Shape;

        [Transpile]
        public sealed record Square(int Side) : Shape;

        [Transpile]
        public static class Areas
        {
            public static int Area(Shape shape) =>
                shape switch
                {
                    Circle c when c.Radius > 5 => c.Radius * 3,
                    Circle c => c.Radius,
                    Square s => s.Side * s.Side,
                };

            public static string Name(Shape shape)
            {
                return shape switch
                {
                    Circle => "circle",
                    _ when shape is Square => "square",
                    _ => throw new InvalidOperationException("x"),
                };
            }

            public static string Simple(int value) => value switch { 1 => "one", _ => "many" };

            public static Func<Shape, int> Lambda() => shape => shape switch { Circle c => c.Radius, Square s => s.Side };

            public static int Nested(Shape shape) => 1 + shape switch { Circle c => c.Radius, Square s => s.Side };

            public static string OfConstant()
            {
                const int k = 2;

                return k switch { 1 => "one", 2 => "two", var v when v > 5 => "big", _ => "other" };
            }
        }
        """;

    private static string Output() => TranspileHelper.Transpile(Source)["areas.ts"];

    [Test]
    public async Task ReturnedSwitch_BindsTheScrutineeOnceAndReturnsFromEachArm()
    {
        var output = Output();

        await Assert.That(output).Contains("let $s = shape;");
        await Assert.That(output).Contains("if ($s instanceof Circle) {");
        await Assert.That(output).Contains("const c = $s;");
        await Assert.That(output).Contains("throw new SwitchExpressionException($s);");
    }

    [Test]
    public async Task ReturnedSwitch_KeepsTheGuardInsideItsArm()
    {
        await Assert.That(Output()).Contains("if (c.radius > 5) {");
    }

    [Test]
    public async Task ReturnedSwitchWithThrowArm_ThrowsThroughTheHelper()
    {
        await Assert
            .That(Output())
            .Contains(
                "return shape instanceof Circle ? \"circle\" : shape instanceof Square ? \"square\" : throwError(new InvalidOperationException(\"x\"));"
            );
    }

    [Test]
    public async Task ReturnedTernaryShapedSwitch_StaysAConditionalChain()
    {
        await Assert.That(Output()).Contains("return value === 1 ? \"one\" : \"many\";");
    }

    [Test]
    public async Task SwitchOutsideReturn_LowersToAConditionalChain()
    {
        var output = Output();

        await Assert
            .That(output)
            .Contains(
                "return (1 + ($s = shape, $s instanceof Circle && ($c = $s, true) ? $c.radius : $s instanceof Square && ($s1 = $s, true) ? $s1.side : throwError(new SwitchExpressionException($s)))) | 0;"
            );
    }

    // A `const` would keep the literal type of a C# constant (`2`), and the
    // type checker would reject the `1` arm as a comparison with no overlap.
    [Test]
    public async Task ReturnedSwitchOverAConstant_BindsTheScrutineeWithLet()
    {
        await Assert.That(Output()).Contains("let $s = k;");
    }
}
