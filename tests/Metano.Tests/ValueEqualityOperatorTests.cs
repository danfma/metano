using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# <c>==</c> / <c>!=</c> over records and tuples compare by value, where
/// JS <c>===</c> compares references (#254). A record comparison lowers to the
/// null-safe runtime <c>valueEquals</c>; a tuple comparison to a conjunction
/// (or disjunction) of element comparisons, after evaluating every operand
/// element once, left to right. Class reference equality, null checks and
/// records lowered to a target primitive (<c>[Branded]</c>) keep the plain
/// operators.
/// </summary>
public class ValueEqualityOperatorTests
{
    private const string Types = """
        [Transpile]
        public record Point(int X, int Y);

        [Transpile]
        public readonly record struct Size(int W, int H);

        [Transpile, PlainObject]
        public record Dto(string Name);

        [Transpile, Branded]
        public readonly record struct UserId(string Value);

        [Transpile]
        public class Node
        {
            public int V;
        }

        [Transpile]
        public class Counter
        {
            public int Calls;

            public int Next() => ++Calls;

            public (int, int) Pair() => (++Calls, 10);
        }
        """;

    [Test]
    public async Task EqualityOperators_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            {{Types}}

            [Transpile]
            public class Comparisons
            {
                public bool Records(Point p, Point? q) => p == q;

                public bool RecordsDiffer(Point p, Point q) => p != q;

                public bool RecordStructs(Size a, Size b) => a == b;

                public bool LiftedRecordStructs(Size? a, Size b) => a == b;

                public bool PlainObjects(Dto a, Dto b) => a == b;

                public bool BrandedPrimitives(UserId a, UserId b) => a == b && (a, 1) == (b, 1);

                public bool NullCheck(Point? p) => p == null || null != p;

                public bool References(Node a, Node b) => a == b;

                public bool Tuples((int, string) a, (int, string) b) => a == b;

                public bool TuplesDiffer((int x, int y) a) => a != (1, 2);

                public bool TupleOfRecords((Point, int) a, (Point, Node) b) => a.Item1 == b.Item1 && (a.Item1, b.Item2) == (b.Item1, b.Item2);

                public bool NestedTuples(((int, int), int) a) => a == ((1, 2), 3);

                public bool EvaluatedOnce(Counter c) => c.Pair() == (c.Next(), 10);

                public bool DecimalElements((decimal, int) a, decimal d) => a == (d, 1) && (1, 2m) != (1, d);

                public bool TupleEqualsMethod((int, int) a, (int, int) b) => a.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("value-equality-operators.ts"));
    }

    [Test]
    public async Task NullableTupleEquality_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Lifted((int, int)? a, (int, int) b) => a == b;
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(1);
    }
}
