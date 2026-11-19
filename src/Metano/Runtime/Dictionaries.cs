// Declarative BCL → JavaScript mappings for the System.Collections.Generic dictionary
// family (spec 011 FR-007, FR-012, stage 011c): the `metano-runtime` `Dictionary` class,
// keyed by elementEquals / elementHashCode or the comparer, with the live `KeyCollection`
// / `ValueCollection` views (contracts/runtime-collections.md). Every member is a plain
// rename; the indexer (`dict[key]` → `get`, `dict[key] = value` → `set`) is lowered by the
// target's element-access bridge, not here.

using Metano.Annotations;

// ─── Count / Keys / Values ──────────────────────────────────
// Roslyn binds `IDictionary<K,V>.Count` and `IReadOnlyDictionary<K,V>.Count` to the
// inherited collection `Count`; the extractor rebinds a `Count` read on one of the three
// dictionary types to that type, and folds `dict.Keys.Count` / `dict.Values.Count` into
// `dict.Count` and `dict.Keys.Contains(k)` into `dict.ContainsKey(k)` (FR-012).

[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Values),
    JsProperty = "values"
)]

// ─── KeyCollection / ValueCollection (live views) ───────────
[assembly: MapProperty(
    typeof(Dictionary<,>.KeyCollection),
    nameof(Dictionary<int, int>.KeyCollection.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(Dictionary<,>.ValueCollection),
    nameof(Dictionary<int, int>.ValueCollection.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>.KeyCollection),
    nameof(Dictionary<int, int>.KeyCollection.Contains),
    JsMethod = "contains",
    DartMethod = "contains"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>.KeyCollection),
    nameof(Dictionary<int, int>.KeyCollection.CopyTo),
    JsMethod = "copyTo"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>.ValueCollection),
    nameof(Dictionary<int, int>.ValueCollection.CopyTo),
    JsMethod = "copyTo"
)]

// ─── Lookups and updates ────────────────────────────────────
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.ContainsValue),
    JsMethod = "containsValue"
)]
[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Add), JsMethod = "add")]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.TryAdd),
    JsMethod = "tryAdd"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Clear),
    JsMethod = "clear"
)]

// ─── TryGetValue → tryGetValue(key, default) ────────────────
// The compiler lowers a call with `out` arguments to a call returning `[result, out…]`
// (IrOutArgumentCall) and keeps one argument slot per `out` parameter filled with C#'s
// `default(T)` for it, the value C# stores in `value` on a miss.
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
// ConcurrentDictionary<K,V> is stage 011h's: until then a native Map and its helper.
[assembly: MapMethod(
    typeof(System.Collections.Concurrent.ConcurrentDictionary<,>),
    nameof(System.Collections.Concurrent.ConcurrentDictionary<int, int>.TryGetValue),
    JsTemplate = "tryGetValue($this, $0, $1)",
    RuntimeImports = "tryGetValue"
)]

// ─── GetValueOrDefault ──────────────────────────────────────
// `GetValueOrDefault` is an extension on IReadOnlyDictionary<K,V> (CollectionExtensions).
// The extractor normalizes it to `(key, defaultValue)` and, for a stage 011c receiver,
// names the receiver's own type as the member's declaring type
// (IntrinsicBclLoweringTable.GetValueOrDefault): the `Dictionary` class has the member,
// the interfaces read it through `tryGetValue` (contracts/runtime-collections.md,
// interface receivers). Any other receiver (an 011h dictionary, still a native Map) keeps
// the runtime helper, which tests membership with `has`.

[assembly: MapMethod(
    typeof(Dictionary<,>),
    "GetValueOrDefault",
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    "GetValueOrDefault",
    JsTemplate = "$this.tryGetValue($0, $1)[1]",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(IReadOnlyDictionary<,>),
    "GetValueOrDefault",
    JsTemplate = "$this.tryGetValue($0, $1)[1]",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(CollectionExtensions),
    nameof(CollectionExtensions.GetValueOrDefault),
    JsTemplate = "getValueOrDefault($this, $0, $1)",
    RuntimeImports = "getValueOrDefault",
    WhenArgCount = 2
)]

// ─── Constructors ───────────────────────────────────────────
// Full arity (contracts/runtime-collections.md): a comparer is the second parameter of
// every factory; the copy constructors from an IDictionary<K,V> or an
// IEnumerable<KeyValuePair<K,V>> are `from`, which throws on null and on a duplicate key.

[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Dictionary<$T0, $T1>()",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.withCapacity<$T0, $T1>($0)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Dictionary<$T0, $T1>(null, $0)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.withCapacity<$T0, $T1>($0, $1)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(int), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.from($0)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(IDictionary<,>) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.from($0)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.from($0, $1)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(IDictionary<,>), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Dictionary.from($0, $1)",
    RuntimeImports = "Dictionary",
    WhenParameterTypes = new[] { typeof(IEnumerable<>), typeof(IEqualityComparer<>) }
)]

// ─── KeyValuePair<K,V> ──────────────────────────────────────
// A KeyValuePair is a `[key, value]` tuple at runtime (the shape a Dictionary iterates),
// so construction builds the tuple and Key / Value read its positions. The struct's
// implicit parameterless constructor (`default` key and value) stays unmapped.

[assembly: MapMethod(
    typeof(KeyValuePair<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1] as [$T0, $T1]",
    WhenArgCount = 2
)]
[assembly: MapProperty(
    typeof(KeyValuePair<,>),
    nameof(KeyValuePair<int, int>.Key),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(KeyValuePair<,>),
    nameof(KeyValuePair<int, int>.Value),
    JsTemplate = "$this[1]"
)]
