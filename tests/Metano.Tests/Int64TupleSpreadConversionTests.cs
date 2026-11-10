using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tuple and spread conversions across a 64-bit integer (spec 012 FR-010):
/// C# converts a tuple element by element wherever Roslyn reports the
/// conversion (an assignment, a return, an argument, the operands of a tuple
/// <c>==</c>, the argument of <c>Equals</c>), and a spread element by element,
/// so each element takes the scalar conversion its types need. The runtime
/// side runs against .NET in <c>samples/SampleInt64Conformance</c>
/// (<c>TupleConversions.cs</c>, fixture <c>tuples.json</c>).
/// </summary>
public class Int64TupleSpreadConversionTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        [Transpile]
        public class Pairs
        {
            public (long, long) Returned((int, int) pair) => pair;

            public long Argument((int, int) pair) => Sum(pair);

            public long Sum((long, long) pair) => pair.Item1 + pair.Item2;

            public (long, long) Assigned((int, int) pair)
            {
                (long, long) wide = pair;

                return wide;
            }

            public (long, long) Evaluated(Func<(int, int)> next) => next();

            public ((long, long), string) Nested(((int, int), string) pair) => pair;

            public (long?, long) NullableElement((int?, int) pair) => pair;

            public (long, long)? NullableTuple((int, int)? pair) => pair;

            public (int, int) Narrowed((long, long) pair) => ((int, int))pair;

            public (int, int) NarrowedChecked((long, long) pair) => checked(((int, int))pair);

            public bool EqualsOperator((long, long) wide, (int, int) narrow) => wide == narrow;

            public bool NotEqualsOperator((int, int) narrow, (long, long) wide) => narrow != wide;

            public bool EqualsLiteral((long, long) wide) => wide == (1, 2);

            public bool EqualsMethod((long, long) wide, (int, int) narrow) => wide.Equals(narrow);

            public bool SameTypes((long, long) a, (long, long) b) => a == b;

            public long Iterated(List<(int, int)> pairs)
            {
                long total = 0;

                foreach ((long, long) pair in pairs)
                    total += pair.Item1 + pair.Item2;

                return total;
            }

            public int IteratedNarrowed(List<(long, long)> pairs)
            {
                var total = 0;

                foreach ((int, int) pair in pairs)
                    total += pair.Item1 + pair.Item2;

                return total;
            }

            public int IteratedNarrowedChecked(List<(long, long)> pairs)
            {
                var total = 0;

                checked
                {
                    foreach ((int, int) pair in pairs)
                        total += pair.Item1 + pair.Item2;
                }

                return total;
            }

            public long IteratedNullable(List<(int, int)?> pairs)
            {
                long total = 0;

                foreach ((long, long) pair in pairs)
                    total += pair.Item1 + pair.Item2;

                return total;
            }

            public int IteratedLifted(List<(int, int)?> pairs)
            {
                var missing = 0;

                foreach ((long, long)? pair in pairs)
                    missing += pair is null ? 1 : 0;

                return missing;
            }

            public (long, long) UnwrappedElement((int?, int) pair) => ((long, long))pair;

            public ((long, long), long) UnwrappedNested(((int, int)?, int) pair) =>
                (((long, long), long))pair;

            public (long, long)? LiftedCast((int, int)? pair) => ((long, long)?)pair;

            public (long, long) LiteralUnwrapped(int? first, int second) =>
                ((long, long))(first, second);

            public ((long, long), long) LiteralNested(int? first, int second) =>
                (((long, long), long))((first, second), second);

            public (int, int) LiteralNarrowed(long first) => ((int, int))(first, 5L);

            public (int, int) LiteralNarrowedChecked(long first) =>
                checked(((int, int))(first, 5L));

            public ((long, long), long) LiteralHoldingATuple((int, int) pair, int last) =>
                (((long, long), long))(pair, last);

            public (long, long) LiteralWrapped(int second) => ((long, long))((int?)2, (int?)second);

            public (long, long) CastLiteralReturned(long a, long b) => ((int, int))(a, b);

            public (long, long)? CastLiteralReturnedNullable(long a, long b) => ((int, int))(a, b);

            public (long, long) CastLiteralAssigned(long a, long b)
            {
                (long, long) wide = ((int, int))(a, b);

                return wide;
            }

            public long CastLiteralArgument(long a, long b) => Sum(((int, int))(a, b));

            public List<(long, long)> CastLiteralElement(long a, long b) => [((int, int))(a, b)];

            public (long, long) CastLiteralArm(bool narrow, long a, long b) =>
                narrow ? ((int, int))(a, b) : (a, b);

            public ((long, long), int) CastLiteralNested(long a, long b) =>
                (((int, int))(a, b), 3);

            public (long, long) CastTupleAssigned((long, long) pair)
            {
                (long, long) wide = ((int, int))pair;

                return wide;
            }

            public (long, long)? NullableLiteral() => (1, 2);

            public (long, long)? Forgiven((int, int)? pair) => pair!;
        }

        [Transpile]
        public class Spreads
        {
            public List<long> ToList(List<int> ints) => [.. ints];

            public HashSet<long> ToSet(int[] ints) => [.. ints];

            public long[] ToArray(int[] ints) => [.. ints, 3];

            public List<(long, long)> ToPairs(List<(int, int)> pairs) => [.. pairs];

            public List<long> Same(List<long> longs) => [.. longs];
        }
        """;

    /// <summary>A converted string spread, which only TypeScript reports: Dart prints no conversion.</summary>
    private const string DartOnlySource = """

        [Transpile]
        public class Codes
        {
            public List<long> Of(string text) => [.. text];
        }
        """;

    [Test]
    public async Task TupleConversions_RebuildTheTupleElementByElement()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await TranspileHelper.AssertGolden("int64-tuple-conversions.ts", files["pairs.ts"]);

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task SpreadConversions_MapEachElement()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await TranspileHelper.AssertGolden("int64-spread-conversions.ts", files["spreads.ts"]);

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A string spreads its UTF-16 code units, which the mapped spread does
    /// not read as C# does, so a converted one is reported instead of emitted
    /// unconverted. (A dictionary spreads key-value pairs, which C# never
    /// converts numerically, so it has no such case.)
    /// </summary>
    [Test]
    public async Task ConvertedStringSpread_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Codes
            {
                public List<long> Of(string text) => [.. text];
            }
            """
        );

        var messages = UnsupportedFeatures(diagnostics).Select(d => d.Message).ToList();

        await Assert.That(messages).HasSingleItem();

        await Assert
            .That(messages[0])
            .IsEqualTo(
                "Expression '.. text' (spread of a string whose characters convert to a 64-bit "
                    + "integer) is not supported by the transpiler."
            );
    }

    /// <summary>
    /// Explicit tuple casts under nullable analysis. The cast of a nullable
    /// tuple reads its value first, with the .NET throw when it is missing.
    /// A cast-literal element unwraps the same way whatever the analysis
    /// says — after a check, after <c>!</c>, read from an <c>await</c> — and
    /// a nullable target slot keeps its lift; only a value wrapped where it
    /// is written (<c>(int?)2</c>, <c>(int?)x</c>) converts with no unwrap.
    /// </summary>
    [Test]
    public async Task ExplicitTupleCasts_UnderNullableAnalysis_UnwrapOrLift()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            #nullable enable
            using System.Threading.Tasks;

            [Transpile]
            public class Unwrapping
            {
                public (long, long) Of((int, int)? pair) => ((long, long))pair;

                public (long, long) OfWrapped(int second) => ((long, long))((int?)2, (int?)second);

                public (long, long) OfWidenedWrap(int second) => ((int, long))(1, ((long?)second));

                public (long, long) OfChecked(int? first, int second) =>
                    first is null ? (0, 0) : ((long, long))(first, second);

                public (long, long) OfForgiven(int? first, int second) =>
                    ((long, long))(first!, second);

                public async Task<(double, double)> OfAwaited(Task<long?> pending, long first)
                {
                    var second = await pending;

                    return ((double, double))(first, second);
                }

                public async Task<((long, long)?, long)> OfAwaitedTuple(
                    Task<(int, int)?> pending,
                    int last
                )
                {
                    var pair = await pending;

                    return (((long, long)?, long))(pair, last);
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("int64-tuple-nullable-cast.ts", files["unwrapping.ts"]);

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A cast literal needs no type of its own: an element without one
    /// (<c>default</c>, <c>null</c>) takes the target's, and the others
    /// convert where they are written.
    /// </summary>
    [Test]
    public async Task CastTupleLiteralWithATypelessElement_ConvertsTheOthers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            #nullable enable

            [Transpile]
            public class Typeless
            {
                public (long, long?) OfDefault(int? first) => ((long, long?))(first, default);

                public (long, (long, string?)) OfNull(int? first, int second) =>
                    ((long, (long, string?)))(first, (second, null));
            }
            """
        );

        await TranspileHelper.AssertGolden("int64-tuple-typeless-cast.ts", files["typeless.ts"]);

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>A reported string spread takes no element name: the next spread of the body is the first.</summary>
    [Test]
    public async Task ConvertedStringSpread_TakesNoElementName()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Codes
            {
                public int Of(string text, int[] ints)
                {
                    List<long> codes = [.. text];
                    List<long> longs = [.. ints];

                    return codes.Count + longs.Count;
                }
            }
            """
        );

        await Assert
            .That(files["codes.ts"])
            .Contains(
                "const longs = [...Array.from(ints, (element$temp$0) => BigInt(element$temp$0))];"
            );
    }

    /// <summary>
    /// A cast literal with more elements than a flat tuple holds is not
    /// converted element by element: the literal's own extraction reports
    /// it, at the top level (with the conversion's report) and nested.
    /// </summary>
    [Test]
    public async Task CastTupleLiteralOverMoreThanSevenElements_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class WideLiterals
            {
                public long Top(int a)
                {
                    var wide = ((long, long, long, long, long, long, long, long))(a, a, a, a, a, a, a, a);

                    return wide.Item8;
                }

                public long Nested(int a)
                {
                    var wide = (((long, long, long, long, long, long, long, long), long))(
                        (a, a, a, a, a, a, a, a),
                        a
                    );

                    return wide.Item2;
                }
            }
            """
        );

        var messages = UnsupportedFeatures(diagnostics).Select(d => d.Message).ToList();

        const string literal =
            "Expression '(a, a, a, a, a, a, a, a)' (tuple literal with more than 7 elements) is "
            + "not supported by the transpiler.";

        const string conversion =
            "Expression '((long, long, long, long, long, long, long, long))(a, a, a, a, a, a, a, "
            + "a)' (tuple conversion over more than 7 elements) is not supported by the "
            + "transpiler.";

        // The top-level cast reports the literal and its conversion, as it
        // did before literals converted in place; the nested one reports the
        // literal once.
        await Assert.That(messages).IsEquivalentTo(new[] { literal, conversion, literal });
    }

    [Test]
    public async Task TupleConversionOverMoreThanSevenElements_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Wide
            {
                public (long, long, long, long, long, long, long, long) Of(
                    (int, int, int, int, int, int, int, int) row
                ) => row;
            }
            """
        );

        var messages = UnsupportedFeatures(diagnostics).Select(d => d.Message).ToList();

        await Assert.That(messages).HasSingleItem();

        await Assert
            .That(messages[0])
            .IsEqualTo(
                "Expression 'row' (tuple conversion over more than 7 elements) is not supported "
                    + "by the transpiler."
            );
    }

    /// <summary>
    /// A <c>foreach</c> converts through its loop variable, so the report
    /// sits on the variable's type, not on the whole loop.
    /// </summary>
    [Test]
    public async Task TupleForEachOverMoreThanSevenElements_IsReportedAtTheVariableType()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Wide
            {
                public long Of(List<(int, int, int, int, int, int, int, int)> rows)
                {
                    long total = 0;

                    foreach ((long, long, long, long, long, long, long, long) row in rows)
                        total += row.Item8;

                    return total;
                }
            }
            """
        );

        var reports = UnsupportedFeatures(diagnostics);

        await Assert.That(reports).HasSingleItem();

        await Assert
            .That(reports[0].Message)
            .IsEqualTo(
                "Expression '(long, long, long, long, long, long, long, long)' (tuple conversion "
                    + "over more than 7 elements) is not supported by the transpiler."
            );

        await Assert
            .That(LocatedSource.TextOf(reports[0]))
            .IsEqualTo("(long, long, long, long, long, long, long, long)");
    }

    /// <summary>Dart's <c>int</c> holds both widths, so its records and spreads stay as they are.</summary>
    [Test]
    public async Task Dart_KeepsTheTupleAndTheSpread()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(Source + DartOnlySource);

        await Assert
            .That(files["pairs.dart"])
            .Contains("(int, int) returned((int, int) pair) => pair;");

        await Assert.That(files["spreads.dart"]).Contains("[...ints]");
        await Assert.That(files["spreads.dart"]).DoesNotContain("element$temp");
        await Assert.That(files["codes.dart"]).Contains("[...text.split('')]");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
