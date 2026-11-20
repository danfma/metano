// Declarative BCL → JavaScript mappings for the sorted collections (spec 011 FR-054,
// stage 011h): `SortedSet<T>`, `SortedDictionary<K, V>` (with its live key and value
// views, the runtime `KeyCollection` / `ValueCollection`) and `SortedList<K, V>` are the
// `metano-runtime` classes of the same names, ordered by the comparer argument or
// `Comparer<T>.Default` (contracts/runtime-collections.md § SortedSet, § SortedDictionary,
// § SortedList). Members are plain renames to the camelCase runtime member; the indexers
// are the element-access bridge's `get` / `set`. `SortedSet<T>.Min` / `Max` read
// `minOr` / `maxOr` with C#'s `default(T)`, which the extractor appends
// (IntrinsicBclLoweringTable), as the `Try*` lowering appends it for an `out`. Members
// the runtime does not implement (`CopyTo(array, index, count)`, `Capacity`,
// `TrimExcess`, `CreateSetComparer`) have no mapping and report MS0001.

using Metano.Annotations;

// ─── SortedSet<T> ────────────────────────────────────────────
[assembly: MapProperty(typeof(SortedSet<>), nameof(SortedSet<int>.Count), JsProperty = "count")]
[assembly: MapProperty(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.Comparer),
    JsProperty = "comparer"
)]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Min), JsMethod = "minOr")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Max), JsMethod = "maxOr")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.ExceptWith),
    JsMethod = "exceptWith"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.GetViewBetween),
    JsMethod = "getViewBetween"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.IntersectWith),
    JsMethod = "intersectWith"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.IsSubsetOf),
    JsMethod = "isSubsetOf"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Overlaps), JsMethod = "overlaps")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Remove), JsMethod = "remove")]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.RemoveWhere),
    JsMethod = "removeWhere"
)]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Reverse), JsMethod = "reverse")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.SetEquals), JsMethod = "setEquals")]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.SymmetricExceptWith),
    JsMethod = "symmetricExceptWith"
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.UnionWith), JsMethod = "unionWith")]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.CopyTo),
    JsMethod = "copyTo",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    nameof(SortedSet<int>.CopyTo),
    JsMethod = "copyTo",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedSet<$T0>()",
    RuntimeImports = "SortedSet",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedSet<$T0>(null, $0)",
    RuntimeImports = "SortedSet",
    WhenParameterTypes = new[] { typeof(IComparer<>) }
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedSet.from($0)",
    RuntimeImports = "SortedSet",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
[assembly: MapMethod(
    typeof(SortedSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedSet.from($0, $1)",
    RuntimeImports = "SortedSet",
    WhenParameterTypes = new[] { typeof(IEnumerable<>), typeof(IComparer<>) }
)]

// ─── SortedDictionary<K, V> ─────────────────────────────────
[assembly: MapProperty(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Comparer),
    JsProperty = "comparer"
)]
[assembly: MapProperty(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.ContainsValue),
    JsMethod = "containsValue"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.CopyTo),
    JsMethod = "copyTo"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    nameof(SortedDictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    "GetValueOrDefault",
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapProperty(
    typeof(SortedDictionary<,>.KeyCollection),
    nameof(SortedDictionary<int, int>.KeyCollection.Count),
    JsProperty = "count"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>.KeyCollection),
    nameof(SortedDictionary<int, int>.KeyCollection.CopyTo),
    JsMethod = "copyTo"
)]
[assembly: MapProperty(
    typeof(SortedDictionary<,>.ValueCollection),
    nameof(SortedDictionary<int, int>.ValueCollection.Count),
    JsProperty = "count"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>.ValueCollection),
    nameof(SortedDictionary<int, int>.ValueCollection.CopyTo),
    JsMethod = "copyTo"
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedDictionary<$T0, $T1>()",
    RuntimeImports = "SortedDictionary",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedDictionary<$T0, $T1>(null, $0)",
    RuntimeImports = "SortedDictionary",
    WhenParameterTypes = new[] { typeof(IComparer<>) }
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedDictionary.from($0)",
    RuntimeImports = "SortedDictionary",
    WhenParameterTypes = new[] { typeof(IDictionary<,>) }
)]
[assembly: MapMethod(
    typeof(SortedDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedDictionary.from($0, $1)",
    RuntimeImports = "SortedDictionary",
    WhenParameterTypes = new[] { typeof(IDictionary<,>), typeof(IComparer<>) }
)]

// ─── SortedList<K, V> ────────────────────────────────────────
[assembly: MapProperty(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.Comparer),
    JsProperty = "comparer"
)]
[assembly: MapProperty(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapMethod(typeof(SortedList<,>), nameof(SortedList<int, int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(SortedList<,>), nameof(SortedList<int, int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.ContainsValue),
    JsMethod = "containsValue"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.GetKeyAtIndex),
    JsMethod = "getKeyAtIndex"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.GetValueAtIndex),
    JsMethod = "getValueAtIndex"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.IndexOfKey),
    JsMethod = "indexOfKey"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.IndexOfValue),
    JsMethod = "indexOfValue"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.SetValueAtIndex),
    JsMethod = "setValueAtIndex"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    nameof(SortedList<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    "GetValueOrDefault",
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedList<$T0, $T1>()",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedList.withCapacity<$T0, $T1>($0)",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new SortedList<$T0, $T1>(null, $0)",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new[] { typeof(IComparer<>) }
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedList.withCapacity<$T0, $T1>($0, $1)",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new[] { typeof(int), typeof(IComparer<>) }
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedList.from($0)",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new[] { typeof(IDictionary<,>) }
)]
[assembly: MapMethod(
    typeof(SortedList<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "SortedList.from($0, $1)",
    RuntimeImports = "SortedList",
    WhenParameterTypes = new[] { typeof(IDictionary<,>), typeof(IComparer<>) }
)]
