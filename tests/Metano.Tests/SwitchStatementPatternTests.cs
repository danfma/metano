namespace Metano.Tests;

/// <summary>
/// Pattern labels of <c>switch</c> statements and the designators of
/// <c>is</c> patterns (#239, legacy FR-013). A switch with pattern, guarded,
/// NaN or <c>null</c> labels lowers to <c>switch (true)</c> with one boolean
/// test per label, the subject evaluated once and each single-label section
/// binding its designators in a block. <c>is</c>-pattern designators become
/// typed <c>let</c>s declared ahead of the statement, in the scope C# gives
/// them, and are assigned by the test itself.
/// </summary>
public class SwitchStatementPatternTests
{
    private const string Shapes = """
        [Transpile]
        public abstract class Shape { }

        [Transpile]
        public sealed class Circle(double r) : Shape
        {
            public double R { get; } = r;
        }

        [Transpile]
        public sealed class Square(double side) : Shape
        {
            public double Side { get; } = side;
        }

        [Transpile]
        public sealed class Node(int value, Node? next)
        {
            public int Value { get; } = value;
            public Node? Next { get; } = next;
        }
        """;

    [Test]
    public async Task PatternLabels_LowerToBooleanCaseTests()
    {
        var result = TranspileHelper.Transpile(
            Shapes
                + """

                [Transpile]
                public static class Describer
                {
                    public static string Describe(object? o)
                    {
                        switch (o)
                        {
                            case null:
                                return "null";
                            case Circle c when c.R > 10:
                                return "big circle " + c.R;
                            case Circle c:
                                return "circle " + c.R;
                            case Square:
                            case int:
                                return "square or int";
                            case string { Length: > 3 } text:
                                return "long " + text;
                            default:
                                return "other";
                        }
                    }

                    public static string Classify(int n)
                    {
                        var result = "";
                        switch (n)
                        {
                            case 0:
                                result = "zero";
                                break;
                            default:
                                result = "many";
                                break;
                            case > 5 and < 10:
                                result = "mid";
                                break;
                            case 1:
                            case 2:
                                result = "small";
                                break;
                        }
                        return result;
                    }

                    public static string OfCurrent(Shape[] shapes)
                    {
                        switch (shapes[0])
                        {
                            case Square s when s.Side > 2:
                                return "big square " + s.Side;
                            case Circle:
                                return "circle";
                            default:
                                return "other";
                        }
                    }
                }
                """
        );

        var expected = TranspileHelper.ReadExpected("switch-statement-patterns.ts");

        await Assert.That(result["describer.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task IsPatternDesignators_DeclareTypedLetsInTheirCSharpScope()
    {
        var result = TranspileHelper.Transpile(
            Shapes
                + """

                [Transpile]
                public static class Geometry
                {
                    public static double Area(Shape shape)
                    {
                        if (shape is Circle c)
                            return c.R * c.R * 3;
                        if (!(shape is Square s))
                            return -1;
                        return s.Side * s.Side;
                    }

                    public static double Radius(Shape shape)
                    {
                        if (shape is not Circle c)
                            return -1;
                        return c.R;
                    }

                    public static bool SameRadius(Shape a, Shape b) =>
                        a is Circle x && b is Circle y && x.R == y.R;

                    public static int Walk(Node? head)
                    {
                        var total = 0;
                        var cursor = head;
                        while (cursor is Node x)
                        {
                            total += x.Value;
                            cursor = x.Next;
                        }
                        cursor = head;
                        while (cursor is Node x)
                        {
                            total += x.Value * 10;
                            cursor = x.Next;
                        }
                        return total;
                    }

                    public static int Blocks(object a, object b)
                    {
                        var total = 0;
                        {
                            if (a is int n)
                                total += n;
                        }
                        {
                            if (b is int n)
                                total += n * 100;
                        }
                        return total;
                    }

                    public static double First(Shape[] shapes) =>
                        shapes[0] is Circle c ? c.R : 0;

                    public static Func<Shape, double> RadiusOf => s => s is Circle c ? c.R : 0;

                    public static int Head(int[] values) =>
                        values is [var first, .. var rest] ? first + rest.Length : -1;
                }
                """
        );

        var expected = TranspileHelper.ReadExpected("is-pattern-designators.ts");

        await Assert.That(result["geometry.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task NullCaseLabel_MatchesAnAbsentValueToo()
    {
        var output = TranspileDescriber(
            """
            public static string Describe(string? s)
            {
                switch (s)
                {
                    case null: return "none";
                    case "a": return "a";
                    default: return "other";
                }
            }
            """
        );

        await Assert.That(output).Contains("switch (true)");
        await Assert.That(output).Contains("case s == null:");
        await Assert.That(output).Contains("case s === \"a\":");
    }

    [Test]
    public async Task ConstantLabels_KeepThePlainSwitch()
    {
        var output = TranspileDescriber(
            """
            public static string Describe(int n)
            {
                switch (n)
                {
                    case 1:
                    case 2: return "small";
                    default: return "other";
                }
            }
            """
        );

        await Assert.That(output).Contains("switch (n)");
        await Assert.That(output).DoesNotContain("switch (true)");
    }

    [Test]
    public async Task GuardThatReassignsTheSubjectLocal_HoistsTheSubject()
    {
        var output = TranspileDescriber(
            """
            public static string Describe(object o)
            {
                var current = o;
                switch (current)
                {
                    case int n when (current = "changed") is string:
                        return "int " + n;
                    case string s:
                        return "string " + s;
                    default:
                        return "other";
                }
            }
            """
        );

        await Assert.That(output).Contains("const $subject = current;");
        await Assert.That(output).Contains("case typeof $subject === \"string\":");
    }

    [Test]
    public async Task NestedPatternSwitches_EachHoistInTheirOwnBlock()
    {
        var output = TranspileDescriber(
            """
            public static string Describe(object[] items)
            {
                switch (items[0])
                {
                    case Circle c:
                        switch (items[1])
                        {
                            case Circle d when d.R == c.R:
                                return "same";
                            default:
                                return "different";
                        }
                    default:
                        return "other";
                }
            }
            """
        );

        await Assert.That(output).Contains("const $subject = items[0]!;");
        await Assert.That(output).Contains("const $subject = items[1]!;");
        await Assert.That(output).Contains("((d) => d.r === c.r)($subject)");
    }

    [Test]
    public async Task ForLoopDesignatorReadByTheIncrement_IsFreshPerIteration()
    {
        // C# gives each iteration its own `n`, which the increment reads: the
        // increment moves to the end of a body that declares `n`.
        var output = TranspileDescriber(
            """
            public static int Count(Node? head)
            {
                var count = 0;
                for (var cursor = head; cursor is Node n; cursor = n.Next)
                    count += n.Value;
                return count;
            }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    for (let cursor = head; ; ) {
                      let n: Node;

                      if (!(cursor instanceof Node && (n = cursor, true))) {
                        break;
                      }

                      count += n.value;
                      cursor = n.next;
                    }
                """
            );
    }

    [Test]
    public async Task ForLoopWithContinue_KeepsTheIncrementInTheHeader()
    {
        var output = TranspileDescriber(
            """
            public static int Count(Node? head)
            {
                var count = 0;
                for (var cursor = head; cursor is Node n; cursor = n.Next)
                {
                    if (n.Value < 0)
                        continue;
                    count += n.Value;
                }
                return count;
            }
            """
        );

        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let n: Node;
                      for (let cursor = head; cursor instanceof Node && (n = cursor, true); cursor = n.next) {
                """
            );
    }

    [Test]
    public async Task TypeUsedOnlyInALoop_IsImportedAsAValue()
    {
        var output = TranspileDescriber(
            """
            public static int Walk(Node? cursor)
            {
                var count = 0;
                while (cursor is Node n)
                {
                    count += n.Value;
                    cursor = n.Next;
                }
                return count;
            }
            """
        );

        await Assert.That(output).Contains("import { Node } from \"./node\";");
    }

    [Test]
    public async Task PatternWithoutDesignator_DeclaresNothing()
    {
        var output = TranspileDescriber(
            """
            public static bool IsRound(Shape shape)
            {
                if (shape is Circle)
                    return true;
                return false;
            }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "static isRound(shape: Shape): boolean {\n    if (shape instanceof Circle) {"
            );
    }

    [Test]
    public async Task CatchAllLabelWithoutDefault_BecomesTheDefaultCase()
    {
        var output = TranspileDescriber(
            """
            public static string Use(object o) => o.ToString() ?? "";

            public static string Describe(object[] items)
            {
                switch (items[0])
                {
                    case int i:
                        return "int " + i;
                    case var other:
                        return Use(other);
                }
            }
            """
        );

        await Assert.That(output).Contains("const $subject = items[0]!;");
        await Assert
            .That(output)
            .Contains(
                """
                        default:
                          {
                            const other = $subject;
                """
            );
    }

    [Test]
    public async Task FieldKeywordGetterSubject_IsCapturedOnce()
    {
        var output = TranspileDescriber(
            """
            public static int Tick
            {
                get
                {
                    field++;
                    return field;
                }
            }

            public static bool Positive() => Tick is int n and > 0;
            """
        );

        await Assert.That(output).Contains("($subject0 = Describer.tick, true)");
    }

    [Test]
    public async Task DesignatorInAFieldInitializer_IsDeclaredInsideAnImmediatelyInvokedArrow()
    {
        var output = TranspileDescriber(
            """
            public static Shape Pick() => new Circle(9);

            public static readonly double Seed = Pick() is Circle c ? c.R : 0;
            """
        );

        await Assert.That(output).Contains("let $subject0: Shape;");
        await Assert.That(output).Contains("let c: Circle;");
        await Assert
            .That(output)
            .Contains(
                "return ($subject0 = Describer.pick(), true) && $subject0 instanceof Circle && (c = $subject0, true) ? c.r : 0;"
            );
        await Assert.That(output).DoesNotContain("/* TODO: unsupported");
    }

    [Test]
    public async Task HandWrittenGetterSubject_IsCapturedOnce()
    {
        var output = TranspileDescriber(
            """
            private static int _reads;

            public static Shape Current
            {
                get
                {
                    _reads++;
                    return new Circle(1);
                }
            }

            public static Shape Stored { get; } = new Circle(2);

            public static double Read() => Current is Circle c ? c.R : 0;

            public static double ReadStored() => Stored is Circle c ? c.R : 0;
            """
        );

        await Assert.That(output).Contains("($subject0 = Describer.current, true)");
        await Assert
            .That(output)
            .Contains("Describer.stored instanceof Circle && (c = Describer.stored, true)");
    }

    [Test]
    public async Task DartSwitchStatement_PrintsPatternLabelsNatively()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            "using Metano.Annotations;\n"
                + Shapes
                + """

                [Transpile]
                public static class Describer
                {
                    public static string Describe(object? o)
                    {
                        switch (o)
                        {
                            case Circle c when c.R > 10:
                                return "big";
                            case > 5:
                                return "number";
                            default:
                                return "other";
                        }
                    }

                    public static double Radius(Shape s) => s is Circle c ? c.R : 0;
                }
                """
        );

        var output = files["describer.dart"];

        await Assert.That(output).Contains("case Circle c when c.r > 10:");
        await Assert.That(output).Contains("case > 5:");
        await Assert.That(output).Contains("      default:\n        return 'other';");
    }

    private static string TranspileDescriber(string members) =>
        TranspileHelper.Transpile(
            Shapes
                + $$"""

                [Transpile]
                public static class Describer
                {
                {{members}}
                }
                """
        )["describer.ts"];
}
