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

                public static bool Named((int, int) first, (int x, int y) second) =>
                    object.Equals(objB: first, objA: second);
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
        await Assert.That(output).Contains("return tupleEquals(first, second);");
        await Assert.That(output).DoesNotContain("objectEquals");
    }

    /// <summary>
    /// Tuples of different <c>ValueTuple</c> instantiations are different
    /// types, never equal in C#, while their arrays could compare equal
    /// element by element, so they keep <c>objectEquals</c>.
    /// </summary>
    [Test]
    public async Task ObjectEqualsOverMismatchedTupleTypes_KeepsObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Widened((int, int) t, (int, long) u) => object.Equals(t, u);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return objectEquals(t, u);");
    }

    /// <summary>
    /// A tuple element that is itself a tuple compares element-wise at any
    /// depth (#331): the call carries the static element shape so the runtime
    /// <c>tupleEquals</c> can tell a nested tuple (compare by value) from a
    /// C# array (compare by reference) — at runtime both are JS arrays.
    /// Element names are compile-time only, so named and unnamed elements of
    /// one instantiation lower to the same shape.
    /// </summary>
    [Test]
    public async Task ObjectEqualsOverNestedTuples_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Nested(((int, int), int) t, ((int, int), int) u) =>
                    object.Equals(t, u);

                public static bool Deeper((((int, int), int), int) t, (((int, int), int), int) u) =>
                    object.Equals(t, u);

                public static bool Named(((int a, int b), int y) t, ((int, int), int) u) =>
                    object.Equals(t, u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(t, u, [[], null]);");
        await Assert.That(output).Contains("return tupleEquals(t, u, [[[], null], null]);");
    }

    /// <summary>
    /// The instance <c>.Equals</c> over nested tuples carries the same
    /// element shape as the <c>object.Equals</c> retarget.
    /// </summary>
    [Test]
    public async Task InstanceEqualsOverNestedTuples_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Nested(((int, int), int) t, ((int, int), int) u) => t.Equals(u);

                public static bool Deeper((((int, int), int), int) t, (((int, int), int), int) u) =>
                    t.Equals(u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(t, u, [[], null]);");
        await Assert.That(output).Contains("return tupleEquals(t, u, [[[], null], null]);");
    }

    /// <summary>
    /// A tuple whose element is an array has no tuple element: no shape is
    /// emitted (byte-identical flat call) and the runtime keeps comparing
    /// that element by reference, as C# compares an array.
    /// </summary>
    [Test]
    public async Task TupleWithAnArrayElement_EmitsTheFlatCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool ArrayElement((int[], int) t, (int[], int) u) =>
                    object.Equals(t, u);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return tupleEquals(t, u);");
    }

    /// <summary>
    /// <c>object.Equals</c> over nullable tuples of the same instantiation
    /// lowers to the same element-wise call: the runtime guards the null
    /// cases C# answers (both null <c>true</c>, one null <c>false</c>, else
    /// element-wise). A nullable tuple element is marked as a tuple in the
    /// shape; each operand is still evaluated once.
    /// </summary>
    [Test]
    public async Task ObjectEqualsOverNullableTuples_RetargetsToTupleEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool BothNullable((int, int)? t, (int, int)? u) => object.Equals(t, u);

                public static bool OneNullable((int, int)? t, (int, int) u) => object.Equals(t, u);

                public static bool Nested(((int, int)?, int)? t, ((int, int)?, int)? u) =>
                    object.Equals(t, u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(t, u);");
        await Assert.That(output).Contains("return tupleEquals(t, u, [[], null]);");
        await Assert.That(output).DoesNotContain("objectEquals");
    }

    /// <summary>
    /// <c>t.Equals(u)</c> over nullable tuples is <c>Nullable&lt;T&gt;.Equals</c>:
    /// both null <c>true</c>, one null <c>false</c>, else element-wise — the
    /// same guarded call. Through <c>.Value</c> the receiver is proven
    /// non-null first, so the underlying tuple's own <c>Equals</c> applies.
    /// </summary>
    [Test]
    public async Task NullableTupleInstanceEquals_RetargetsToTupleEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Receiver((int, int)? t, (int, int)? u) => t.Equals(u);

                public static bool ThroughValue(((int, int), int)? t, ((int, int), int) u) =>
                    t!.Value.Equals(u);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(t, u);");
        await Assert.That(output).Contains("tupleEquals(t, u, [[], null]);");
        await Assert.That(output).DoesNotContain("objectEquals");
    }

    /// <summary>
    /// A boxed tuple (an operand statically <c>object</c>) cannot be told
    /// apart from a C# array at runtime — both are JS arrays — so it keeps
    /// the reference comparison of the general <c>objectEquals</c> lowering.
    /// This pins the limitation rather than the C# answer (value equality).
    /// </summary>
    [Test]
    public async Task BoxedTuples_CompareByReference_KeepObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Boxed(object a, object b) => object.Equals(a, b);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return objectEquals(a, b);");
    }

    /// <summary>
    /// Against an operand that is not statically a tuple (an array of equal
    /// elements, or an <c>object</c> that may hold one), the element-wise
    /// comparison could say <c>true</c> where C# says <c>false</c>. Both keep
    /// <c>objectEquals</c>.
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
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains("return objectEquals(values, [1, 2] as [number, number]);");

        await Assert.That(output).Contains("return objectEquals(t, o);");
    }
}
