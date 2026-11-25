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
}
