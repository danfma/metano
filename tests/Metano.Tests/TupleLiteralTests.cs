using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# tuple literals lower to a positional array asserted to the TS tuple
/// type, and tuple elements read by name or as <c>ItemN</c> lower to the
/// element index (#254, #314).
/// </summary>
public class TupleLiteralTests
{
    [Test]
    public async Task TupleLiteralsAndElementReads_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public record Point(int X, int Y);

            [Transpile]
            public class Pairs
            {
                public (int, string) Positional(int a) => (a, "x");

                public (int x, int y) Named() => (x: 1, y: 2);

                public (string? Name, Point At) Converted(Point p) => (null, p);

                public int Reads()
                {
                    var named = Named();
                    var positional = Positional(3);
                    var local = (first: 4, second: "s");
                    return named.x + named.Item2 + positional.Item1 + positional.Item2.Length
                        + local.first + local.second.Length;
                }
            }
            """
        );

        await Assert
            .That(result["pairs.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("tuple-literals.ts"));
    }

    /// <summary>
    /// A C# tuple is copied on assignment, a TS array is shared and a Dart
    /// record is immutable, so an element write is rejected on every target.
    /// </summary>
    [Test]
    public async Task TupleElementWrites_ReportUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Writes
            {
                public static int Write()
                {
                    var t = (a: 1, b: 2);
                    t.a = 5;
                    t.Item2 += 1;
                    t.b++;
                    return t.a;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(3);
    }

    /// <summary>
    /// A tuple element or key of a collection lookup compares by value in C#
    /// and by reference once lowered to an array, so each such use warns; the
    /// lowering itself still emits.
    /// </summary>
    [Test]
    public async Task TupleKeyedCollectionLookups_ReportUnsupported()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Lookups
            {
                public static bool Find(List<(int, string)> list, List<string> names)
                {
                    var keys = new Dictionary<(int, int), string>();
                    HashSet<(int, int)> seen = new();
                    var byName = new Dictionary<string, (int, int)>();
                    return list.Contains((1, "a")) && list.IndexOf((2, "b")) >= 0
                        && names.Contains("c") && byName.ContainsKey("d") && keys.Count == seen.Count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(4);
        await Assert.That(result["lookups.ts"]).Contains("list.includes([1, \"a\"]");
    }

    [Test]
    public async Task TupleDeconstructionIntoExistingVariables_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Swaps
            {
                public static int Swap(int x, int y)
                {
                    (x, y) = (y, x);
                    return x - y;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(1);
    }

    [Test]
    public async Task TupleLiteralWiderThanSevenElements_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Wide
            {
                public static object Make() => (1, 2, 3, 4, 5, 6, 7, 8);
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(1);
    }

    /// <summary>
    /// <c>object.Equals</c> over two operands that are statically tuples
    /// compares element by element through the first tuple's own
    /// <c>Equals</c> (<c>tupleEquals</c>), never by reference as the general
    /// <c>objectEquals</c> lowering does for an array.
    /// </summary>
    [Test]
    public async Task ObjectEqualsOverTwoTuples_ComparesElementWise()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Literals(int a) => object.Equals((a, "x"), (a, "x"));

                public static bool Locals((int, int) t, (int, int) u) => object.Equals(t, u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains(
                "return tupleEquals([a, \"x\"] as [number, string], [a, \"x\"] as [number, string]);"
            );

        await Assert.That(output).Contains("return tupleEquals(t, u);");
    }

    /// <summary>
    /// Against an operand that is not statically a tuple (an array of equal
    /// elements, or an <c>object</c> that may hold one), the element-wise
    /// comparison could say <c>true</c> where C# says <c>false</c>; a
    /// nullable tuple may be <c>null</c>. All keep <c>objectEquals</c>.
    /// </summary>
    [Test]
    public async Task ObjectEqualsWithANonTupleOperand_KeepsObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool WithArray(int[] values) => object.Equals(values, (1, 2));

                public static bool WithObject((int, int) t, object o) => object.Equals(t, o);

                public static bool NullableTuple((int, int)? t, (int, int) u) => object.Equals(t, u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains("return objectEquals(values, [1, 2] as [number, number]);");

        await Assert.That(output).Contains("return objectEquals(t, o);");
        await Assert.That(output).Contains("return objectEquals(t, u);");
    }
}
