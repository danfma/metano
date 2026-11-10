using Metano.Compiler.Diagnostics;

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

    /// <summary>The same construction over 64-bit elements.</summary>
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
    /// A string spread yields the string's <c>char</c>s, its UTF-16 code
    /// units: <c>...s.split("")</c>. A JS string's own iterator yields code
    /// points, so <c>[..."h😀i"]</c> has 3 elements where C# has 4 (#588).
    /// The operand is parenthesized where <c>.split</c> would bind tighter.
    /// The golden type-checks under <c>tsgo --strict</c>.
    /// </summary>
    [Test]
    public async Task StringSpread_YieldsUtf16CodeUnits()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Letters
            {
                public List<char> Of(string text) => [.. text];

                public char[] Framed(string text, string? suffix) =>
                    ['<', .. text, .. suffix ?? "", .. text + "!", '>'];

                public HashSet<char> Distinct(string text) => [.. text];

                public List<string> Words(string[] words) => [.. words];

                public string Dashed(string[] words) => string.Join("-", words);
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("string-spread-code-units.ts", result["letters.ts"]);
    }

    /// <summary>
    /// TypeScript infers an array literal's element type from its elements,
    /// so a literal C# types wider keeps the declared type (#590): a typed
    /// local is annotated (<c>const l: (number | null)[] = [1]</c>, an empty
    /// <c>const l: number[] = []</c>), and a literal no declaration types —
    /// a <c>var</c> array creation, a cast, a lambda's result, a nested
    /// literal — is asserted (<c>[1] as (number | null)[]</c>). A literal whose
    /// elements say the type, or that a field, parameter, return type or
    /// assignment target types, stays bare. The golden type-checks under
    /// <c>tsgo --strict --noImplicitOverride</c>.
    /// </summary>
    [Test]
    public async Task ListLiteral_TypedWiderThanItsElements_KeepsTheDeclaredType()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            #nullable enable
            using System;
            using System.Collections.Generic;

            [Transpile]
            public record Animal(string Name);

            [Transpile]
            public record Dog(string Name, bool Barks) : Animal(Name);

            [Transpile]
            public record Cat(string Name, int Lives) : Animal(Name);

            [Transpile]
            public class Holder
            {
                public List<int?> Items { get; set; } = [];

                public List<int?> Initial { get; } = [1];
            }

            [Transpile]
            public class Lists
            {
                private readonly List<int?> _field = [1];
                private List<Animal> _pets = [];

                public int NullableLocal()
                {
                    List<int?> l = [1];
                    l.Add(null);
                    return l.Count;
                }

                public int PolymorphicLocal(Dog dog, Cat cat)
                {
                    List<Animal> l = [dog];
                    l.Add(cat);
                    return l.Count;
                }

                public int EmptyLocal()
                {
                    List<int> l = [];
                    l.Add(1);
                    return l.Count;
                }

                public int NullableReference(string? maybe)
                {
                    List<string?> named = ["a"];
                    List<string?> same = [maybe];
                    named.Add(null);
                    return named.Count + same.Count;
                }

                public int SpreadLocal(int[] xs)
                {
                    List<int?> l = [.. xs];
                    l.Add(null);
                    return l.Count;
                }

                public int Reassigned()
                {
                    IEnumerable<int?> l = [1];
                    l = [null];
                    return new List<int?>(l).Count;
                }

                public int NullableArray()
                {
                    int?[] a = [1];
                    a[0] = null;
                    return a.Length;
                }

                public int ArrayCreation(Dog dog, Cat cat)
                {
                    var a = new Animal[] { dog };
                    var same = new[] { dog };
                    var empty = new int[] { };
                    a[0] = cat;
                    return a.Length + same.Length + empty.Length;
                }

                public int Nested()
                {
                    List<List<int?>> l = [[1]];
                    l[0].Add(null);
                    return l[0].Count;
                }

                public int Cast()
                {
                    var l = (List<int?>)[1];
                    l.Add(null);
                    return l.Count;
                }

                public int Conditional(bool flag)
                {
                    List<int?> l = flag ? [1] : [2, 3];
                    l.Add(null);
                    return l.Count;
                }

                public int Lambda()
                {
                    Func<List<int?>> make = () => [1];
                    var l = make();
                    l.Add(null);
                    return l.Count;
                }

                public int Exact()
                {
                    List<int> ints = [1, 2];
                    List<double> doubles = [1, 2.5];
                    List<string> names = ["a"];
                    return ints.Count + doubles.Count + names.Count;
                }

                public int ReadOnlyViews(Dog dog)
                {
                    IReadOnlyCollection<int> counted = [];
                    IEnumerable<Animal> sequence = [dog];
                    ICollection<int?> collection = [1];
                    IReadOnlyList<Animal> read = [dog];
                    IList<int?> list = [1];
                    collection.Add(null);
                    list[0] = null;
                    var first = read[0].Name.Length;
                    return counted.Count + collection.Count + read.Count + list.Count + first;
                }

                public int ErasedCasts()
                {
                    List<int?> cast = [(int?)1];
                    List<int?> defaulted = [default];
                    cast.Add(null);
                    defaulted.Add(1);
                    return cast.Count + defaulted.Count;
                }

                public int NestedNullability()
                {
                    List<(int, string?)> pairs = [(1, "a")];
                    List<(int, string)> exact = [(1, "a")];
                    pairs.Add((2, null));
                    return pairs.Count + exact.Count;
                }

                public int ForHeader()
                {
                    var total = 0;

                    for (List<int?> l = [1]; l.Count < 3; l.Add(null))
                        total += l.Count;

                    return total;
                }

                public int Numeric(int[] ints)
                {
                    byte[] bytes = [1, 2];
                    List<long> longs = [1, 2];
                    List<long> widened = [.. ints];
                    return bytes.Length + longs.Count + widened.Count;
                }

                public int Walk(List<int?>? given, int kind)
                {
                    List<int?> parenthesized = ([1]);
                    List<int?> coalesced = given ?? [1];
                    List<int?> switched = kind switch { 0 => [1], _ => [2, 3] };
                    Func<List<int?>> block = () =>
                    {
                        return [1];
                    };
                    var holder = new Holder { Items = [1] };
                    var lists = new List<List<int?>> { ([1]) };
                    parenthesized.Add(null);
                    coalesced.Add(null);
                    switched.Add(null);
                    block().Add(null);
                    holder.Items.Add(null);
                    holder.Initial.Add(null);
                    lists[0].Add(null);
                    return parenthesized.Count + coalesced.Count + switched.Count + lists.Count;
                }

                public List<int?> Returned() => [1];

                public List<int?> ReturnedFromABlock()
                {
                    return [];
                }

                public int Take(List<int?> l) => l.Count;

                public int Argument() => Take([1]);

                public int Assigned(Dog dog, Cat cat)
                {
                    _pets = [dog];
                    _pets.Add(cat);
                    _field.Add(null);
                    return _pets.Count + _field.Count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("list-literal-element-types.ts", result["lists.ts"]);
    }

    /// <summary>
    /// Dart declares a local with its C# type and takes a literal's type from
    /// it, so the declared element type changes nothing there (#590).
    /// </summary>
    [Test]
    public async Task ListLiteral_TypedWiderThanItsElements_IsUnchangedInDart()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile]
            public class Lists
            {
                public int Nullable()
                {
                    List<int?> l = [1];
                    l.Add(null);
                    return l.Count;
                }

                public int Empty()
                {
                    List<int> l = [];
                    return l.Count;
                }

                public int ReadOnly()
                {
                    IReadOnlyCollection<int?> l = [1];
                    return l.Count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("list-literal-element-types.dart", files["lists.dart"]);
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
