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

        await Assert
            .That(files["pairs.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-tuple-conversions.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task SpreadConversions_MapEachElement()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert
            .That(files["spreads.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-spread-conversions.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A string spreads its characters and a dictionary its pairs, which the
    /// mapped spread does not read as C# does, so a converted one is reported
    /// instead of emitted unconverted.
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
        await Assert.That(messages[0]).Contains("spread of a string or dictionary");
    }

    /// <summary>Dart's <c>int</c> holds both widths, so its records and spreads stay as they are.</summary>
    [Test]
    public async Task Dart_KeepsTheTupleAndTheSpread()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        await Assert
            .That(files["pairs.dart"])
            .Contains("(int, int) returned((int, int) pair) => pair;");

        await Assert.That(files["spreads.dart"]).Contains("[...ints]");
        await Assert.That(files["spreads.dart"]).DoesNotContain("element$temp");
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
