// Declarative BCL → JavaScript mappings for System.MemoryExtensions.
//
// C# 14 converts an array to ReadOnlySpan<T> / Span<T> implicitly (first-class spans), so
// `array.Contains(x)`, `array.IndexOf(x)` or `array.SequenceEqual(other)` bind to the span
// extensions of MemoryExtensions rather than to System.Linq.Enumerable. A span lowers to
// the same JavaScript array as the array it views, so an overload whose JS array method
// compares as the List<T> mapping does lowers to that method. Every other
// MemoryExtensions call — a comparer or StringComparison overload, SequenceEqual,
// StartsWith, Sort, BinarySearch, IndexOfAny, ContainsAny, Count, … — has no mapping and
// reports MS0001 (the extractor's unlowered-span-call check) instead of emitting a member
// call JavaScript arrays do not have.
//
// WhenArgCount = 1 selects the overload that takes the value alone. An int?[] receiver
// binds the comparer overload (its element is not IEquatable<T>) with the comparer
// omitted, which still writes one argument and keeps the default comparison.

using Metano.Annotations;

// ─── Contains / IndexOf / LastIndexOf ───────────────────────
// The same lowering as List<T>.Contains / IndexOf (see Lists.cs): `includes` compares by
// SameValueZero (NaN finds NaN, like double.Equals), `indexOf` / `lastIndexOf` by `===`.
// Neither compares records by value. The span-valued IndexOf / LastIndexOf overload
// (`array.IndexOf(otherArray)`, a subsequence search) also writes one argument; the
// filter cannot tell it from the value overload, so it lowers to `indexOf` as it did
// before this mapping.

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
