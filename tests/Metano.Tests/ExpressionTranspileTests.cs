namespace Metano.Tests;

public class ExpressionTranspileTests
{
    [Test]
    public async Task WithExpression_GeneratesSpread()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Coord(int X, int Y)
            {
                public static Coord MoveX(Coord coord, int dx) =>
                    coord with { X = coord.X + dx };
            }
            """
        );

        await TranspileHelper.AssertGolden("with-expression.ts", result["coord.ts"]);
    }

    [Test]
    public async Task IfElseThrow_GeneratesCorrectStatements()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Pair(int A, int B)
            {
                public static void Check(Pair pair)
                {
                    if (pair.A != pair.B)
                        throw new System.Exception("mismatch");
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("if-else-throw.ts", result["pair.ts"]);
    }
}
