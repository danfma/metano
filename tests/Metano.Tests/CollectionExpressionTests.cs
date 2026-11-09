using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// A set or dictionary collection expression constructs the runtime
/// collection from one iterable (<c>new HashSet([a, ...b])</c>,
/// <c>new Map([[k, v], ...pairs])</c>), so its elements and spreads are
/// evaluated once, in source order (#538). Spreading the elements into the
/// constructor call (<c>new Map(...pairs)</c>) handed it one entry as the
/// iterable and dropped the rest.
/// </summary>
public class CollectionExpressionTests
{
    [Test]
    public async Task SetAndDictionaryCollectionExpressions_MatchTheGoldenOutput()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile, StringEnum]
            public enum Color { Red, Green }

            [Transpile]
            public class Shelf
            {
                private readonly HashSet<int> _primes = [2, 3, 5];
                private readonly HashSet<Color> _colors = [Color.Red];
                private readonly ImmutableDictionary<string, int> _defaults = [new("a", 1), new("b", 2)];
                private readonly ConcurrentDictionary<string, int> _cache = [];
                private readonly ImmutableHashSet<string> _none = [];
                private int _calls;

                private int Next() => ++_calls;

                public HashSet<int> Union(int[] xs, List<int> ys) => [.. xs, .. ys];

                public HashSet<int> Mixed(int[] xs) => [Next(), .. xs, Next()];

                public SortedSet<int> Sorted(HashSet<int> source) => [.. source, 0];

                public ImmutableHashSet<string> Names(IEnumerable<string> names) => ["root", .. names];

                public int Count(HashSet<string> set) => set.Count;

                public int Pass(string[] extra) => Count(["a", .. extra]);

                public HashSet<Color> Palette(HashSet<Color> extra)
                {
                    HashSet<Color> palette = [Color.Green, .. extra, .. _colors];
                    return palette;
                }

                public ImmutableDictionary<string, int> Merge(
                    Dictionary<string, int> left,
                    IEnumerable<KeyValuePair<string, int>> right
                ) => [.. left, new KeyValuePair<string, int>("k", 0), .. right, .. _defaults];

                public ImmutableDictionary<long, string> ByIds(List<KeyValuePair<long, string>> pairs) =>
                    [.. pairs];
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();
        await Assert
            .That(result["shelf.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("collection-expressions.ts"));
    }

    /// <summary>
    /// A BCL dictionary has no single-argument <c>Add</c>: a non-empty
    /// collection expression over one compiles only through a user-defined
    /// <c>Add(KeyValuePair)</c>, whose duplicate-key behaviour a <c>Map</c>
    /// cannot assume, so it is reported instead of lowered.
    /// </summary>
    [Test]
    public async Task DictionaryCollectionExpression_ThroughAUserDefinedAdd_IsReported()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            public static class PairAdding
            {
                public static void Add<K, V>(this Dictionary<K, V> map, KeyValuePair<K, V> pair)
                    where K : notnull => map.Add(pair.Key, pair.Value);

                public static void Add<K, V>(this SortedDictionary<K, V> map, KeyValuePair<K, V> pair)
                    where K : notnull => map.Add(pair.Key, pair.Value);
            }

            [Transpile]
            public class Holder
            {
                private readonly Dictionary<string, int> _empty = [];

                public Dictionary<string, int> Copy(List<KeyValuePair<string, int>> pairs) =>
                    [new("x", 1), .. pairs];

                public SortedDictionary<string, int> Sorted(List<KeyValuePair<string, int>> pairs) =>
                    [.. pairs];
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(unsupported).Count().IsEqualTo(2);
        await Assert
            .That(unsupported[0])
            .IsEqualTo(
                "Collection expression '[new(\"x\", 1), .. pairs]' builds "
                    + "'System.Collections.Generic.Dictionary<string, int>' through a user-defined "
                    + "Add(KeyValuePair) method, whose semantics the transpiler does not reproduce. "
                    + "Build the dictionary with explicit Add calls instead."
            );
        await Assert
            .That(unsupported[1])
            .Contains("'System.Collections.Generic.SortedDictionary<string, int>'");
        await Assert
            .That(result["holder.ts"])
            .Contains("private readonly _empty: Map<string, number> = new Map();");
        await Assert.That(result["holder.ts"]).DoesNotContain("new Map(...");
    }

    /// <summary>The same construction over 64-bit elements, in the BigInt mode.</summary>
    [Test]
    public async Task SetCollectionExpression_WithLongElements_ConstructsFromBigInts()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public class Holder
            {
                public HashSet<long> Ids(long[] more) => [1, 2, .. more];
            }
            """,
            Int64Representation.BigInt
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();
        await Assert.That(result["holder.ts"]).Contains("return new HashSet([1n, 2n, ...more]);");
    }

    /// <summary>
    /// Dart's <c>Set</c> and <c>Map</c> constructors take no elements: the
    /// construction goes through the <c>.of</c> / <c>.fromEntries</c>
    /// factories, and a spread prints as Dart's list spread.
    /// </summary>
    [Test]
    public async Task SetAndDictionaryCollectionExpressions_UseTheIterableFactoriesInDart()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using Metano.Annotations;

            [Transpile]
            public class Shelf
            {
                private readonly HashSet<int> _primes = [2, 3, 5];
                private readonly HashSet<int> _none = [];

                public HashSet<int> Mixed(int[] xs) => [1, .. xs, 2];

                public List<int> Joined(int[] xs) => [0, .. xs];

                public ImmutableDictionary<string, int> Pairs(List<KeyValuePair<string, int>> pairs) =>
                    [new("k", 0), .. pairs];
            }
            """
        );

        var output = files["shelf.dart"];

        await Assert.That(output).Contains("final Set<int> _primes = Set<int>.of([2, 3, 5]);");
        await Assert.That(output).Contains("final Set<int> _none = Set<int>();");
        await Assert.That(output).Contains("=> Set<int>.of([1, ...xs, 2]);");
        await Assert.That(output).Contains("=> [0, ...xs];");
        await Assert
            .That(output)
            .Contains(
                "=> Map<String, int>.fromEntries([MapEntry<String, int>('k', 0), ...pairs]);"
            );
    }
}
