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

                public static bool? Guarded(((int, int), int)? a, ((int, int), int) b) =>
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

                public static bool ThroughObject(((int, int), int) a, object b) => a.Equals(b);

                public static bool Boxed(object a, object b) => object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("tuple-equality.ts"));
    }

    [Test]
    public async Task FlatTuples_KeepTheMainLowering()
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
    /// A tuple element that is itself a tuple compares element-wise at any
    /// depth (#331): the call carries the static element shape so the runtime
    /// <c>tupleEquals</c> can tell a nested tuple (compare by value) from a
    /// C# array (compare by reference) — at runtime both are JS arrays.
    /// </summary>
    [Test]
    public async Task ObjectEqualsOverNestedTuples_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Nested(((int, int), int) a, ((int, int), int) b) =>
                    object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    [Test]
    public async Task ObjectEqualsOverThreeLevelNesting_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Deeper(
                    (((int, int), int), int) a,
                    (((int, int), int), int) b
                ) => object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [\n      [[], null],\n      null,\n    ]);");
    }

    /// <summary>
    /// The instance <c>.Equals</c> over nested tuples carries the same
    /// element shape as the <c>object.Equals</c> lowering.
    /// </summary>
    [Test]
    public async Task InstanceEqualsOverNestedTuples_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Nested(((int, int), int) a, ((int, int), int) b) => a.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    [Test]
    public async Task InstanceEqualsOverThreeLevelNesting_PassesTheElementShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Deeper(
                    (((int, int), int), int) a,
                    (((int, int), int), int) b
                ) => a.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [\n      [[], null],\n      null,\n    ]);");
    }

    /// <summary>
    /// Element names are compile-time only, so named and unnamed elements of
    /// one instantiation lower to the same shape.
    /// </summary>
    [Test]
    public async Task NamedTupleElements_IgnoreNamesInTheShape()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Named(
                    ((int first, int second), int tag) a,
                    ((int, int), int) b
                ) => object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// Only tuple elements are marked in the shape: an array element keeps
    /// comparing by reference, as C# compares an array.
    /// </summary>
    [Test]
    public async Task NestedTupleWithAnArrayElement_MarksOnlyTheTuple()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Mixed(((int, int), int[]) a, ((int, int), int[]) b) =>
                    object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// A nullable tuple element is the tuple or <c>null</c>; the shape marks
    /// it as a tuple and the runtime guards the null cases.
    /// </summary>
    [Test]
    public async Task NullableTupleElement_IsMarkedInTheShape()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool WithNullableElement(((int, int)?, int) a, ((int, int)?, int) b) =>
                    object.Equals(a, b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// Tuples of different <c>ValueTuple</c> instantiations are different
    /// types, never equal in C#, while their arrays could compare equal
    /// element by element, so they keep <c>objectEquals</c>. A nullable-tuple
    /// receiver over such an argument retargets to the static form — the
    /// instance lowering would crash on a positional array.
    /// </summary>
    [Test]
    public async Task MismatchedInstantiations_KeepObjectEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool Widened((int, int) a, (int, long) b) => object.Equals(a, b);

                public static bool NullableReceiver((int, int)? a, (int, long) b) => a.Equals(b);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return objectEquals(a, b);");
        await Assert.That(output).Contains("return objectEquals(a, b);");
    }

    /// <summary>
    /// An <c>Equals(object)</c> call whose argument is not the receiver's
    /// tuple instantiation keeps the plain declarative mapping — the flat
    /// <c>tupleEquals</c> — so its nested elements compare by reference, a
    /// documented limitation (the mapping cannot see static types).
    /// </summary>
    [Test]
    public async Task NestedTupleEqualsWithAnObjectArgument_KeepsTheFlatCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool ThroughObject(((int, int), int) a, object b) => a.Equals(b);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return tupleEquals(a, b);");
    }

    /// <summary>
    /// A generic type parameter is never a tuple here, so <c>T.Equals</c>
    /// keeps the general lowering — the limitation C# generics have no
    /// runtime element shape to lower to.
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

    /// <summary>
    /// A tuple whose element is an array next to a nested tuple: only the
    /// tuple element is marked, and a tuple with no tuple element at all
    /// keeps the byte-identical flat call.
    /// </summary>
    [Test]
    public async Task TupleWithOnlyArrayElements_EmitsTheFlatCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool ArrayElement((int[], int) a, (int[], int) b) =>
                    object.Equals(a, b);
            }
            """
        );

        await Assert.That(result["comparisons.ts"]).Contains("return tupleEquals(a, b);");
    }

    /// <summary>
    /// <c>object.Equals</c> over nullable tuples of the same instantiation
    /// lowers to the same element-wise call: the runtime guards the null
    /// cases C# answers (both null <c>true</c>, one null <c>false</c>, else
    /// element-wise). A <c>null</c> literal operand is never the
    /// instantiation, so it keeps <c>objectEquals</c>, which agrees.
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
                public static bool BothNullable((int, int)? a, (int, int)? b) =>
                    object.Equals(a, b);

                public static bool OneNullable((int, int)? c, (int, int) d) =>
                    object.Equals(c, d);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(a, b);");
        await Assert.That(output).Contains("return tupleEquals(c, d);");
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
                public static bool Receiver((int, int)? a, (int, int)? b) => a.Equals(b);

                public static bool ThroughValue(((int, int), int)? a, ((int, int), int) b) =>
                    a!.Value.Equals(b);
            }
            """
        );

        var output = result["comparisons.ts"];

        await Assert.That(output).Contains("return tupleEquals(a, b);");
        await Assert.That(output).Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// <c>t?.Equals(u)</c> yields <c>null</c> for a null receiver (a nullable
    /// boolean in C#), so the lowering keeps the null-conditional guard and
    /// the comparison runs only on a value — reading the receiver through
    /// the guard, never evaluating it twice.
    /// </summary>
    [Test]
    public async Task ConditionalAccessEquals_LowersNullSafe()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool? Guarded(((int, int), int)? a, ((int, int), int) b) =>
                    a?.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return a == null ? null : tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// A nullable argument boxes to <c>object</c>, so the call binds
    /// <c>Equals(object)</c> — the recognizer sees through the boxing to the
    /// same instantiation.
    /// </summary>
    [Test]
    public async Task EqualsWithANullableArgument_RetargetsToTupleEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public static class Comparisons
            {
                public static bool NullableArgument(((int, int), int) a, ((int, int), int)? b) =>
                    a.Equals(b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// A named argument on the instance form names the only parameter; the
    /// operands keep their source order.
    /// </summary>
    [Test]
    public async Task NamedArgumentOnInstanceEquals_KeepsTheCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Named(((int, int), int) a, ((int, int), int) b) =>
                    a.Equals(other: b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
    }

    /// <summary>
    /// Named static arguments in either order keep the source evaluation
    /// order — the operand written first is lowered first (tuple equality is
    /// symmetric).
    /// </summary>
    [Test]
    public async Task StaticEqualsNamedArguments_KeepSourceOrder()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Comparisons
            {
                public static bool Reversed(((int, int), int) a, ((int, int), int) b) =>
                    object.Equals(objB: a, objA: b);
            }
            """
        );

        await Assert
            .That(result["comparisons.ts"])
            .Contains("return tupleEquals(a, b, [[], null]);");
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
}
