namespace Metano.Tests;

/// <summary>
/// A switch expression outside a <c>return</c> lowers to a conditional chain
/// over temporaries of the enclosing statement list, never a closure (spec 001
/// FR-095, #735): the subject is bound once, the arms are tested in order with
/// their guards, and the tail throws through <c>throwError</c>.
/// </summary>
public class SwitchExpressionChainTests
{
    private const string Shapes = """
        using System;

        [Transpile]
        public abstract record Shape;

        [Transpile]
        public sealed record Circle(int Radius) : Shape;

        [Transpile]
        public sealed record Square(int Side) : Shape;

        """;

    private static Dictionary<string, string> TranspileAll(string members) =>
        TranspileHelper.Transpile(
            Shapes
                + $$"""
                [Transpile]
                public static class Areas
                {
                {{members}}
                }
                """
        );

    private static string Transpile(string members) => TranspileAll(members)["areas.ts"];

    [Test]
    public async Task LocalInitializer_BindsTheSubjectOnceAndThrowsWhenUnmatched()
    {
        var output = Transpile(
            """
                public static int Area(Shape shape)
                {
                    var area = shape switch { Circle c => c.Radius, Square s => s.Side * s.Side };
                    return area + 1;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static area(shape: Shape): number {
                    let $s;
                    let $c: Circle;
                    let $s1: Square;
                    const area = ($s = shape, $s instanceof Circle && ($c = $s, true) ? $c.radius : $s instanceof Square && ($s1 = $s, true) ? Math.imul($s1.side, $s1.side) : throwError(new SwitchExpressionException($s)));

                    return (area + 1) | 0;
                  }
                """
            );

        await Assert.That(output).Contains("import { throwError } from \"metano-runtime\";");
        await Assert.That(output).DoesNotContain("=>");
    }

    [Test]
    public async Task Assignment_TestsGuardsInOrderAfterTheDesignatorIsBound()
    {
        var output = Transpile(
            """
                public static int Area(Shape shape)
                {
                    var area = 0;
                    area = shape switch { Circle c when c.Radius > 5 => c.Radius * 3, Circle c => c.Radius, _ => 0 };
                    return area;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static area(shape: Shape): number {
                    let $s;
                    let $c: Circle;
                    let $c1: Circle;
                    let area = 0;
                    area = ($s = shape, $s instanceof Circle && ($c = $s, $c.radius > 5) ? Math.imul($c.radius, 3) : $s instanceof Circle && ($c1 = $s, true) ? $c1.radius : 0);

                    return area;
                  }
                """
            );
    }

    [Test]
    public async Task Argument_BindsAVarCatchAllInTheTail()
    {
        var output = Transpile(
            """
                public static int Use(int value) => value;

                public static int Area(Shape shape) => Use(shape switch { Circle c => c.Radius, var other => other.GetHashCode() });
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static area(shape: Shape): number {
                    let $s;
                    let $c: Circle;
                    let $other: Shape | null;

                    return Areas.use(($s = shape, $s instanceof Circle && ($c = $s, true) ? $c.radius : ($other = $s, valueHashCode($other))));
                  }
                """
            );
    }

    [Test]
    public async Task NestedInAnArm_TakesItsOwnTemporariesAndThrowsFromAThrowArm()
    {
        var output = Transpile(
            """
                public static string Name(Shape shape, int n) =>
                    "x" + shape switch
                    {
                        Circle c => n switch { 1 => "one", var k when k > c.Radius => "big", _ => throw new InvalidOperationException("n") },
                        _ => "other",
                    };
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static name(shape: Shape, n: number): string {
                    let $s, $s1;
                    let $c: Circle;
                    let $k: number;

                    return "x" + (($s = shape, $s instanceof Circle && ($c = $s, true) ? ($s1 = n, $s1 === 1 ? "one" : ($k = $s1, $k > $c.radius) ? "big" : throwError(new InvalidOperationException("n"))) : "other") ?? "");
                  }
                """
            );
    }

    [Test]
    public async Task NestedInAReturnedSwitch_DoesNotReuseTheBlockScrutinee()
    {
        var output = Transpile(
            """
                public static int Sum(Shape shape, Shape other)
                {
                    return shape switch
                    {
                        Circle c => other switch { Square s => s.Side + c.Radius, _ => 0 },
                        _ => 1,
                    };
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static sum(shape: Shape, other: Shape): number {
                    let $s1;
                    let $s2: Square;

                    {
                      let $s = shape;

                      if ($s instanceof Circle) {
                        const c = $s;

                        return ($s1 = other, $s1 instanceof Square && ($s2 = $s1, true) ? ($s2.side + c.radius) | 0 : 0);
                      }

                      return 1;
                    }
                  }
                """
            );
    }

    // `let $s;` widens what it is assigned: a constant subject keeps no literal
    // type, so the `1` arm type-checks.
    [Test]
    public async Task ConstantSubject_IsBoundToAnUntypedTemporary()
    {
        var output = Transpile(
            """
                public static int Pick()
                {
                    const int k = 2;
                    var r = k switch { 1 => 10, 2 => 20 };
                    return r;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    let $s;
                    const k = 2;
                    const r = ($s = k, $s === 1 ? 10 : $s === 2 ? 20 : throwError(new SwitchExpressionException($s)));
                """
            );
    }

    // A closure in an arm that reads no designator keeps the chain.
    [Test]
    public async Task ClosureInAnArm_ReadingNoDesignator_KeepsTheChain()
    {
        var output = Transpile(
            """
                public static Func<int> Reader(Shape shape)
                {
                    Func<int> f = shape switch { Circle c => c.Radius > 0 ? () => 1 : () => 0, _ => () => -1 };
                    return f;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    let $s;
                    let $c: Circle;
                    const f = ($s = shape, $s instanceof Circle && ($c = $s, true) ? $c.radius > 0 ? () => 1 : () => 0 : () => -1);
                """
            );
    }

    [Test]
    public async Task DesignatorWrittenByTheGuard_WritesTheTemporary()
    {
        var output = Transpile(
            """
                public static int Radius(Shape shape)
                {
                    var r = shape switch { Circle c when (c = new Circle(9)) is not null => c.Radius, _ => 0 };
                    return r;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "const r = ($s = shape, $s instanceof Circle && ($c = $s, ($c = new Circle(9)) != null) ? $c.radius : 0);"
            );
    }

    [Test]
    public async Task PropertyPatternWithDesignators_AssignsThemInTheTest()
    {
        var output = Transpile(
            """
                public static int Radius(Shape shape)
                {
                    var r = shape switch { Circle { Radius: var radius } c when radius > 1 => radius + c.Radius, _ => 0 };
                    return r;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "const r = ($s = shape, $s instanceof Circle && ($c = $s, true) && ($radius = $s.radius, $radius > 1) ? ($radius + $c.radius) | 0 : 0);"
            );
    }

    [Test]
    public async Task AwaitInAnArm_StaysInTheEnclosingAsyncFunction()
    {
        var output = Transpile(
            """
                public static async System.Threading.Tasks.Task<int> Load(Shape shape, System.Threading.Tasks.Task<int> pending)
                {
                    var r = shape switch { Circle c => c.Radius + await pending, _ => 0 };
                    return r;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "const r = ($s = shape, $s instanceof Circle && ($c = $s, true) ? ($c.radius + await pending) | 0 : 0);"
            );
    }

    [Test]
    public async Task ThrowArmOfASubjectReadingChain_ThrowsThroughTheHelper()
    {
        var output = Transpile(
            """
                public static string Echo(string text) => text;

                public static string Name(int value) => Echo(value switch { 1 => "one", _ => throw new ArgumentException("no") });
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return Areas.echo(value === 1 ? \"one\" : throwError(new ArgumentException(\"no\")));"
            );
    }

    // Every `case` clause of a JS `switch` shares one scope: the temporaries
    // of each section go to the list enclosing the `switch`, never redeclared.
    [Test]
    public async Task TwoSwitchSections_DeclareTheirTemporariesOnceAboveTheSwitch()
    {
        var output = Transpile(
            """
                public static int Sections(int sel, Shape s)
                {
                    switch (sel)
                    {
                        case 1: var a = 1 + s switch { Circle c => c.Radius, _ => 0 }; return a;
                        case 2: var b = 2 + s switch { Circle c => c.Radius, _ => 0 }; return b;
                        default: return 0;
                    }
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                  static sections(sel: number, s: Shape): number {
                    let $s, $s1;
                    let $c: Circle;
                    let $c1: Circle;

                    switch (sel) {
                      case 1:
                        const a = (1 + ($s = s, $s instanceof Circle && ($c = $s, true) ? $c.radius : 0)) | 0;

                        return a;
                      case 2:
                        const b = (2 + ($s1 = s, $s1 instanceof Circle && ($c1 = $s1, true) ? $c1.radius : 0)) | 0;

                        return b;
                      default:
                        return 0;
                    }
                  }
                """
            );
    }

    // A closure that reads a designator would share one temporary across the
    // evaluations of a loop header, so the switch keeps fresh arrow bindings.
    [Test]
    public async Task ClosureCapturingADesignator_KeepsTheArrow()
    {
        var output = Transpile(
            """
                public static int Count(Shape[] shapes)
                {
                    var fs = new System.Collections.Generic.List<Func<int>>();
                    var i = 0;
                    Func<int>? f;
                    while ((f = (i < shapes.Length ? shapes[i++] : null) switch { Circle c => () => c.Radius, _ => null }) != null)
                        fs.Add(f);
                    return fs.Count;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    while ((f = (($s) => {
                        if ($s instanceof Circle) {
                          const c = $s;

                          return () => c.radius;
                        }

                        return null;
                      })(i < shapes.length ? shapes[((i = (i + 1) | 0) - 1) | 0]! : null)) != null) {
                """
            );

        await Assert.That(output).DoesNotContain("let $c");
    }

    // The BCL exception lowers its arguments once, so the switch in one
    // declares one set of temporaries.
    [Test]
    public async Task ThrowExpressionWithASwitchArgument_ThrowsThroughTheHelperOnce()
    {
        var output = Transpile(
            """
                public static object Pick(object? o, Shape s)
                {
                    var z = o ?? throw new ArgumentException(s switch { Circle c => "c" + c.Radius, _ => "?" });
                    return z;
                }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    let $s;
                    let $c: Circle;
                    const z = o ?? throwError(new ArgumentException(($s = s, $s instanceof Circle && ($c = $s, true) ? "c" + $c.radius : "?")));
                """
            );
    }

    // A field initializer has no statement list to declare temporaries in.
    [Test]
    public async Task FieldInitializer_KeepsTheArrow()
    {
        var output = Transpile(
            "public static readonly int Field = 3 switch { 1 => 1, var x when x > 2 => 2, _ => 0 };"
        );

        await Assert.That(output).Contains("static readonly field = /* @__PURE__ */ (($s) => {");
    }
}
