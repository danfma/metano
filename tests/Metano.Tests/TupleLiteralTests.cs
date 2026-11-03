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
    /// The full tuple-equality surface in one golden file: flat calls keep
    /// the main lowering, nested and nullable tuples carry the element
    /// shape, and every limitation (mismatched instantiation, an object
    /// argument, boxing) keeps its general lowering.
    /// </summary>
    [Test]
    public async Task TupleValueEquality_MatchesTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Flat((int, int) a, (int, int) b) => object.Equals(a, b);

                public static bool Nested(((int, int), int) a, ((int, int), int) b) =>
                    object.Equals(a, b);

                public static bool Deeper((((int, int), int), int) a, (((int, int), int), int) b) =>
                    object.Equals(a, b);

                public static bool Named(((int x, int y), int tag) a, ((int, int), int) b) =>
                    a.Equals(b);

                public static bool Mixed(((int, int), int[]) a, ((int, int), int[]) b) =>
                    a.Equals(b);

                public static bool NullableBoth(((int, int), int)? a, ((int, int), int)? b) =>
                    object.Equals(a, b);

                public static bool NullableElement(((int, int)?, int) a, ((int, int)?, int) b) =>
                    object.Equals(a, b);

                public static bool NullableReceiver((int, int)? a, (int, int)? b) => a.Equals(b);

                public static bool? Guarded(((int, int), int)? a, ((int, int), int) b) =>
                    a?.Equals(b);

                public static bool? ConditionalMismatched(((int, int), int)? a, (int, long) b) =>
                    a?.Equals(b);

                public static bool NullableArgument(((int, int), int) a, ((int, int), int)? b) =>
                    a.Equals(b);

                public static bool NamedArgument(((int, int), int) a, ((int, int), int) b) =>
                    a.Equals(other: b);

                public static bool ReversedNamed(((int, int), int) a, ((int, int), int) b) =>
                    object.Equals(objB: a, objA: b);

                public static bool ThroughValue(((int, int), int)? a, ((int, int), int) b) =>
                    a!.Value.Equals(b);

                public static bool Mismatched((int, int) a, (int, long) b) => object.Equals(a, b);

                public static bool NullableMismatched((int, int)? a, (int, long) b) => a.Equals(b);

                public static bool NullableMismatchedObject((int, int)? a, object b) =>
                    a.Equals(b);

                public static bool ThroughObject(((int, int), int) a, object b) => a.Equals(b);

                public static bool Boxed(object a, object b) => object.Equals(a, b);

                public static bool Eight(
                    (int, int, int, int, int, int, int, int) a,
                    (int, int, int, int, int, int, int, int) b
                ) => a.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("tuple-equality.ts"));
    }

    /// <summary>
    /// Flat tuples lower to the element-wise call. This differs from main's
    /// unmapped instance form (a crashing <c>a.equals(b)</c> — Dart
    /// <c>a.equals(b)</c> — becomes <c>tupleEquals(a, b)</c> / Dart
    /// <c>a == b</c>); the static form keeps main's <c>tupleEquals</c> via
    /// the retarget byte for byte.
    /// </summary>
    [Test]
    public async Task FlatTuples_LowerToTheElementWiseCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Literals(int a) => object.Equals((a, "x"), (a, "x"));

                public static bool Locals((int, int) c, (int, int) d) => object.Equals(c, d);

                public static bool Instance((int, int) e, (int, int) f) => e.Equals(f);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains(
                "return tupleEquals([a, \"x\"] as [number, string], [a, \"x\"] as [number, string]);"
            );

        await Assert.That(output).Contains("return tupleEquals(c, d);");
        await Assert.That(output).Contains("return tupleEquals(e, f);");
    }

    /// <summary>
    /// A conditional <c>?.Equals</c> whose receiver is not a tuple keeps
    /// the general lowering — the receiver's own <c>equals</c> must still be
    /// the call that runs, including a <c>[Name]</c>-renamed override. This
    /// pins that the recognizer declines rather than hijacking the override
    /// chain root.
    /// </summary>
    [Test]
    public async Task NonTupleConditionalEquals_KeepTheGeneralLowering()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public sealed record Session(string Id);

            [Transpile]
            public sealed class User
            {
                public string Name { get; }

                public User(string name) => Name = name;

                [Name("isSame")]
                public bool Equals(User? other) => Name == other?.Name;
            }

            [Transpile]
            public static class Comparisons
            {
                public static bool? Record(Session? a, Session b) => a?.Equals(b);

                public static bool? Renamed(User? a, User b) => a?.Equals(b);

                public static bool? Stringy(string? a, string b) => a?.Equals(b);

                public static bool? Generic<T>(T? a, T b)
                    where T : class => a?.Equals(b);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains("static record(a: Session | null, b: Session): boolean | null {");
        await Assert.That(output).Contains("return a?.equals(b) ?? null;");
        await Assert.That(output).Contains("return a?.isSame(b) ?? null;");
    }

    /// <summary>
    /// A nullable tuple element is a different element type than the plain
    /// tuple, so the two are not one instantiation and keep
    /// <c>objectEquals</c> (C#: never equal).
    /// </summary>
    [Test]
    public async Task NullableTupleElementVersusPlainTuple_KeepObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Mixed(((int, int)?, int) a, ((int, int), int) b) =>
                    object.Equals(a, b);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return objectEquals(a, b);");
    }

    /// <summary>
    /// Tuples of different <c>ValueTuple</c> instantiations are different
    /// types, never equal in C#. The static form keeps <c>objectEquals</c>;
    /// a nullable-tuple receiver lowers to C#'s answer — <c>false</c>,
    /// unless both operands can be <c>null</c> and are — because the
    /// instance form would crash on a positional array.
    /// </summary>
    [Test]
    public async Task MismatchedInstantiations_KeepTheirLowerings()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Widened((int, int) a, (int, long) b) => object.Equals(a, b);

                public static bool NullableReceiver((int, int)? c, (int, long) d) => c.Equals(d);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return objectEquals(a, b);");
        await Assert.That(output).Contains("return false;");
    }

    /// <summary>
    /// A generic type parameter is never a tuple here, so <c>T.Equals</c>
    /// keeps the general lowering — C# generics have no runtime element
    /// shape to lower to.
    /// </summary>
    [Test]
    public async Task GenericOperands_KeepTheGeneralLowering()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Generic<T>(T a, T b) => object.Equals(a, b);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return objectEquals(a, b);");
    }

    [Test]
    public async Task ObjectEqualsAgainstANullLiteral_KeepsObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool AgainstNull((int, int)? a) => object.Equals(a, null);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return objectEquals(a, null);");
    }

    /// <summary>
    /// Against an operand that is not statically a tuple (an array of equal
    /// elements, or an <c>object</c> that may hold one), the element-wise
    /// comparison could say <c>true</c> where C# says <c>false</c>, so both
    /// keep <c>objectEquals</c>.
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
                public static bool WithArray(int[] a) => object.Equals(a, (1, 2));

                public static bool WithObject((int, int) c, object d) => object.Equals(c, d);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return objectEquals(a, [1, 2] as [number, number]);");

        await Assert.That(output).Contains("return objectEquals(c, d);");
    }

    /// <summary>
    /// A side-effecting conditional receiver is evaluated once: the guard
    /// and the comparison both read the bound value (#331 — the node is
    /// registered with the null-conditional walker).
    /// </summary>
    [Test]
    public async Task ConditionalTupleReceiver_IsEvaluatedOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public class Holder
            {
                public ((int, int), int)? Pair { get; set; }
            }

            [Transpile]
            public static class Comparisons
            {
                public static bool? Guarded(Holder h, ((int, int), int) b) => h.Pair?.Equals(b);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert
            .That(output)
            .Contains("h.pair == null ? null : tupleEquals(h.pair, b, [[], null]);");
    }

    /// <summary>
    /// The node lowers inside an async body — an awaited operand stays an
    /// awaited operand of the comparison.
    /// </summary>
    [Test]
    public async Task AwaitedTupleEqualityOperand_StaysAwaited()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static async Task<bool> Awaited(Task<((int, int), int)> a, ((int, int), int) b) =>
                    object.Equals(await a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(await a, b, [[], null]);");
    }

    /// <summary>
    /// A <c>[PlainObject]</c> instance method lowers through the IR path,
    /// so a tuple equality inside it rewrites <c>this</c> reads like any
    /// other expression.
    /// </summary>
    [Test]
    public async Task PlainObjectTupleEquality_RewritesThisReads()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile, PlainObject]
            public record Line(int X, int Y)
            {
                public bool SameOrigin((int, int) other) => object.Equals((X, Y), other);
            }
            """
        );

        await Assert
            .That(result["line.ts"])
            .Contains("tupleEquals([self.x, self.y] as [number, number], other)");
    }
}
