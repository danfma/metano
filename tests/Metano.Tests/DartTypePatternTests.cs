namespace Metano.Tests;

public class DartTypePatternTests
{
    private const string Source = """
        [Transpile]
        public abstract class Shape;

        [Transpile]
        public class Circle(int radius) : Shape
        {
            public int Radius { get; } = radius;
        }

        [Transpile]
        public class Square(int side) : Shape
        {
            public int Side { get; } = side;
        }

        [Transpile]
        public static class Describer
        {
            public static string Bare(Shape shape) =>
                shape switch { Circle => "circle", Square => "square", _ => "other" };

            public static string Bound(Shape shape) =>
                shape switch { Circle c => c.Radius > 2 ? "big" : "small", _ => "other" };
        }
        """;

    [Test]
    public async Task TypePattern_PrintsAsObjectPattern_744()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(Source);

        await Assert.That(diagnostics).IsEmpty();

        var dart = files["describer.dart"];
        var bare = "Circle() => 'circle', Square() => 'square', _ => 'other'";

        await Assert.That(dart).Contains(bare);
        await Assert.That(dart).Contains("Circle c =>");
    }

    [Test]
    public async Task TypePattern_MatchesInstancesAtRunTime_744()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        var output = await DartToolchain.AnalyzeAndRunAsync(
            files,
            """
            import 'circle.dart';
            import 'square.dart';
            import 'describer.dart';

            void main() {
              var ok = Describer.bare(Circle(1)) == 'circle'
                && Describer.bare(Square(2)) == 'square'
                && Describer.bound(Circle(3)) == 'big';
              if (!ok) throw StateError('type pattern did not match');
              print('OK');
            }
            """
        );

        await Assert.That(output).IsEqualTo("OK");
    }

    private const string NestedSource = """
        [Transpile]
        public class Square(int side)
        {
            public int Side { get; } = side;
        }

        [Transpile]
        public class Pair(Square left)
        {
            public Square Left { get; } = left;
        }

        [Transpile]
        public class Box<T>(T value)
        {
            public T Value { get; } = value;
        }

        [Transpile]
        public static class Matcher
        {
            public static string Statement(object? pair)
            {
                switch (pair)
                {
                    case Pair { Left: Square }:
                        return "square";
                    default:
                        return "other";
                }
            }

            public static bool IsBox(object? o) => o is Box<int> && o.GetHashCode() > 0;

            public static string Tuple(object? o) =>
                o switch { ValueTuple<int, string> => "pair", Func<int> => "func", _ => "other" };
        }
        """;

    [Test]
    public async Task NestedPatternTypes_AreImportedAndRecordPatternsUseATypedWildcard_744()
    {
        var (files, _) = DartTranspileHelper.Transpile(NestedSource);

        await Assert.That(files["matcher.dart"]).Contains("case Pair(left: Square()):");
        await Assert.That(files["matcher.dart"]).Contains("(int, String) _ => 'pair'");
        await Assert.That(files["matcher.dart"]).Contains("int Function() _ => 'func'");

        var output = await DartToolchain.AnalyzeAndRunAsync(
            files,
            """
            import 'box.dart';
            import 'pair.dart';
            import 'square.dart';
            import 'matcher.dart';

            void main() {
              var ok = Matcher.statement(Pair(Square(1))) == 'square'
                && Matcher.isBox(Box<int>(1))
                && !Matcher.isBox('x')
                && Matcher.tuple((1, 'a')) == 'pair'
                && Matcher.tuple(() => 1) == 'func'
                && Matcher.tuple(5) == 'other';
              if (!ok) throw StateError('nested pattern did not match');
              print('OK');
            }
            """
        );

        await Assert.That(output).IsEqualTo("OK");
    }
}
