using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// A set or dictionary collection expression constructs the runtime
/// collection from one iterable (<c>new HashSet&lt;number&gt;([a, ...b])</c>,
/// <c>new Map&lt;string, number&gt;([[k, v], ...pairs])</c>), so its elements
/// and spreads are evaluated once, in source order (#538). Spreading the
/// elements into the constructor call (<c>new Map(...pairs)</c>) handed it one
/// entry as the iterable and dropped the rest.
/// </summary>
public class CollectionExpressionTests
{
    private const string UserDefinedAdd = """
        public static class PairAdding
        {
            public static void Add<K, V>(this Dictionary<K, V> map, KeyValuePair<K, V> pair)
                where K : notnull => map.Add(pair.Key, pair.Value);

            public static void Add<K, V>(this SortedDictionary<K, V> map, KeyValuePair<K, V> pair)
                where K : notnull => map.Add(pair.Key, pair.Value);
        }
        """;

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
            public record Animal(string Name);

            [Transpile]
            public record Dog(string Name, bool Barks) : Animal(Name);

            [Transpile]
            public record Cat(string Name, int Lives) : Animal(Name);

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

                public HashSet<int?> Optional()
                {
                    HashSet<int?> values = [1];
                    values.Add(null);
                    return values;
                }

                public HashSet<Animal> Pets(Dog dog, Cat cat)
                {
                    HashSet<Animal> pets = [dog];
                    pets.Add(cat);
                    return pets;
                }

                public HashSet<int> Filled()
                {
                    HashSet<int> values = [];
                    values.Add(1);
                    return values;
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
    /// A cast names the collection a collection expression builds
    /// (<c>(HashSet&lt;int&gt;)[1, 1]</c>): the construction follows the cast
    /// target, as it follows a declared one.
    /// </summary>
    [Test]
    public async Task CastCollectionExpressions_ConstructTheCastTarget()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;

            {{UserDefinedAdd}}

            [Transpile]
            public class Holder
            {
                public object Set() => (HashSet<int>)[1, 2];

                public int Distinct() => ((HashSet<int>)[1, 1]).Count;

                public object Immutable() => (ImmutableDictionary<string, int>)[new("a", 1)];

                public object Added() => (Dictionary<string, int>)[new("a", 1)];
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        var output = result["holder.ts"];

        await Assert
            .That(unsupported)
            .IsEquivalentTo([
                "Collection expression '[new(\"a\", 1)]' builds "
                    + "'System.Collections.Generic.Dictionary<string, int>' through a user-defined "
                    + "Add(KeyValuePair) method, whose semantics the transpiler does not reproduce. "
                    + "Build the dictionary with explicit Add calls instead.",
            ]);

        await Assert.That(output).Contains("return new HashSet<number>([1, 2]);");

        await Assert.That(output).Contains("return new HashSet<number>([1, 1]).size;");

        await Assert
            .That(output)
            .Contains("return new Map<string, number>([[\"a\", 1] as [string, number]]);");
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
            $$"""
            using System.Collections.Generic;

            {{UserDefinedAdd}}

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

        await Assert
            .That(unsupported)
            .IsEquivalentTo([
                "Collection expression '[new(\"x\", 1), .. pairs]' builds "
                    + "'System.Collections.Generic.Dictionary<string, int>' through a user-defined "
                    + "Add(KeyValuePair) method, whose semantics the transpiler does not reproduce. "
                    + "Build the dictionary with explicit Add calls instead.",
                "Collection expression '[.. pairs]' builds "
                    + "'System.Collections.Generic.SortedDictionary<string, int>' through a "
                    + "user-defined Add(KeyValuePair) method, whose semantics the transpiler does "
                    + "not reproduce. Build the dictionary with explicit Add calls instead.",
            ]);

        await Assert
            .That(result["holder.ts"])
            .Contains("private readonly _empty: Map<string, number> = new Map<string, number>();");

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
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await Assert
            .That(result["holder.ts"])
            .IsEqualTo(
                """
                import { HashSet } from "metano-runtime";

                export class Holder {
                  constructor() { }

                  ids(more: bigint[]): HashSet<bigint> {
                    return new HashSet<bigint>([1n, 2n, ...more]);
                  }
                }

                """
            );
    }

    /// <summary>
    /// Dart's <c>Set</c> and <c>Map</c> constructors take no elements: the
    /// construction goes through the <c>.of</c> / <c>.fromEntries</c>
    /// factories, and a spread prints as Dart's list spread — over a map's
    /// <c>entries</c> and a string's characters, which Dart does not iterate.
    /// </summary>
    [Test]
    public async Task SetAndDictionaryCollectionExpressions_UseTheIterableFactoriesInDart()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
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

                public List<char> Letters(string text) => [.. text, '!'];

                public ImmutableDictionary<string, int> Pairs(
                    List<KeyValuePair<string, int>> pairs,
                    Dictionary<string, int> map
                ) => [new("k", 0), .. pairs, .. map];
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await Assert
            .That(files["shelf.dart"])
            .IsEqualTo(
                """
                class Shelf {
                  final Set<int> _primes = Set<int>.of([2, 3, 5]);

                  final Set<int> _none = Set<int>();

                  Set<int> mixed(List<int> xs) => Set<int>.of([1, ...xs, 2]);

                  List<int> joined(List<int> xs) => [0, ...xs];

                  List<String> letters(String text) => [...text.split(''), '!'];

                  Map<String, int> pairs(List<MapEntry<String, int>> pairs, Map<String, int> map) => Map<String, int>.fromEntries([MapEntry<String, int>('k', 0), ...pairs, ...map.entries]);
                }

                """
            );
    }
}
