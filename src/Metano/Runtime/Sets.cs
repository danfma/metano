// Declarative BCL → JavaScript mappings for the System.Collections.Generic set family
// (spec 011 FR-007, stage 011c): the reworked `metano-runtime` `HashSet` class, keyed by
// elementEquals / elementHashCode or the comparer (contracts/runtime-collections.md).
// Every member is a plain rename; `TryGetValue` takes the value C# stores on a miss,
// which the out-argument lowering appends.

using Metano.Annotations;

// ─── HashSet<T> ──────────────────────────────────────────────
[assembly: MapProperty(typeof(HashSet<>), nameof(HashSet<int>.Count), JsProperty = "count")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Remove), JsMethod = "remove")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.TryGetValue), JsMethod = "tryGetValue")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.RemoveWhere), JsMethod = "removeWhere")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.UnionWith), JsMethod = "unionWith")]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.IntersectWith),
    JsMethod = "intersectWith"
)]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.ExceptWith), JsMethod = "exceptWith")]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.SymmetricExceptWith),
    JsMethod = "symmetricExceptWith"
)]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.IsSubsetOf), JsMethod = "isSubsetOf")]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Overlaps), JsMethod = "overlaps")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.SetEquals), JsMethod = "setEquals")]
[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.CopyTo), JsMethod = "copyTo")]

// ─── ISet<T> / IReadOnlySet<T> ───────────────────────────────
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.UnionWith), JsMethod = "unionWith")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.IntersectWith), JsMethod = "intersectWith")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.ExceptWith), JsMethod = "exceptWith")]
[assembly: MapMethod(
    typeof(ISet<>),
    nameof(ISet<int>.SymmetricExceptWith),
    JsMethod = "symmetricExceptWith"
)]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.IsSubsetOf), JsMethod = "isSubsetOf")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.IsSupersetOf), JsMethod = "isSupersetOf")]
[assembly: MapMethod(
    typeof(ISet<>),
    nameof(ISet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(ISet<>),
    nameof(ISet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Overlaps), JsMethod = "overlaps")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.SetEquals), JsMethod = "setEquals")]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.IsSubsetOf),
    JsMethod = "isSubsetOf"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.Overlaps),
    JsMethod = "overlaps"
)]
[assembly: MapMethod(
    typeof(IReadOnlySet<>),
    nameof(IReadOnlySet<int>.SetEquals),
    JsMethod = "setEquals"
)]

// ─── SortedSet<T> (stage 011h; until then the runtime HashSet) ─
[assembly: MapProperty(typeof(SortedSet<>), nameof(SortedSet<int>.Count), JsProperty = "count")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Remove), JsMethod = "remove")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Clear), JsMethod = "clear")]

// ─── Constructors ─────────────────────────────────────────────
// Full arity, each argument evaluated once (contracts/runtime-collections.md): a comparer
// is the second parameter of every factory, `null` items for the comparer constructor.

[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new HashSet<$T0>()",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "HashSet.withCapacity<$T0>($0)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "HashSet.from($0)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new HashSet<$T0>(null, $0)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "HashSet.withCapacity<$T0>($0, $1)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(int), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "HashSet.from($0, $1)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(IEnumerable<>), typeof(IEqualityComparer<>) }
)]
