// Declarative BCL → JavaScript mappings for System.MemoryExtensions.
//
// C# 14 converts an array (and a string) to ReadOnlySpan<T> / Span<T> implicitly
// (first-class spans), so `array.Contains(x)`, `array.IndexOf(x)` or
// `array.SequenceEqual(other)` bind to the span extensions of MemoryExtensions rather than
// to System.Linq.Enumerable. A span lowers to the value it views: an array, or a string
// for a ReadOnlySpan<char> over a string. An overload whose JavaScript method of the same
// name computes what C# computes lowers to it. Every other MemoryExtensions call — a
// comparer or StringComparison overload, SequenceEqual, the parameterless Sort, BinarySearch,
// IndexOfAny, ContainsAny, Count, … — has no mapping and reports MS0001 (the extractor's
// unlowered-span-call check) instead of emitting a member call that does not exist.
//
// WhenArgCount selects the overload by the arguments the call passes. The extractor
// drops a comparer that is omitted or the `null` constant from every MemoryExtensions call
// (an int?[] receiver binds the comparer overload of Contains, its element is not
// IEquatable<T>), so those calls lower as the comparer-less overload.

using Metano.Annotations;

// ─── Contains / IndexOf / LastIndexOf ───────────────────────
// The same lowering as List<T>.Contains / IndexOf (see Lists.cs): `includes` compares by
// SameValueZero (NaN finds NaN, like double.Equals), `indexOf` / `lastIndexOf` by `===`.
// Neither compares records, record structs, decimals or DateTimes by value. On a string
// receiver they are the string methods, which search a char or a substring. The
// subsequence overload (`array.IndexOf(otherArray)`) also passes one argument; the
// extractor reports it unless the receiver is a string.

[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.Contains),
    JsMethod = "includes",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 1
)]

// ─── String searches on a ReadOnlySpan<char> ────────────────
// `span.StartsWith("ab")`, `span.EndsWith('c')` and the parameterless Trim family exist
// for a span of chars and lower to the string methods. The extractor reports them on a
// receiver that is not a string in JavaScript (an array, char[] included, or a
// Span<char>), where the string method does not exist.

[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.StartsWith),
    JsMethod = "startsWith",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.EndsWith),
    JsMethod = "endsWith",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.Trim),
    JsMethod = "trim",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.TrimStart),
    JsMethod = "trimStart",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.TrimEnd),
    JsMethod = "trimEnd",
    WhenArgCount = 0
)]

// ─── Reverse ────────────────────────────────────────────────
// `span.Reverse()` on a Span<T> reverses the viewed array in place, as Array.reverse
// does. `array.Reverse()` binds Enumerable.Reverse(T[]) (a LINQ stage), not this method.

[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.Reverse),
    JsMethod = "reverse",
    WhenArgCount = 0
)]

// ─── Sort(comparison) ───────────────────────────────────────
// `array.Sort((x, y) => …)` and `span.Sort(comparison)` sort in place with the comparison,
// as Array.sort does with a comparator (the List<T>.Sort mapping). Only the Comparison<T>
// overload: the parameterless Sort would sort numbers as strings and Sort(comparer) has no
// comparator, so both keep reporting. Array.sort is stable where .NET's introsort is not,
// so elements the comparison calls equal may keep a different order.

[assembly: MapMethod(
    typeof(MemoryExtensions),
    nameof(MemoryExtensions.Sort),
    JsMethod = "sort",
    WhenParameterTypes = new[] { typeof(Comparison<>) }
)]
