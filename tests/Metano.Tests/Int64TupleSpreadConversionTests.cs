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

    /// <summary>A converted string spread, which only TypeScript reports: Dart prints no conversion.</summary>
    private const string DartOnlySource = """

        [Transpile]
        public class Codes
        {
            public List<long> Of(string text) => [.. text];
        }
        """;

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
