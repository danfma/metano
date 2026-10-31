// Declarative BCL → JavaScript mappings for the System.Collections.Generic dictionary
// family.
//
// Dictionary<K,V> lowers to JS Map<K,V> at the type level (handled by TypeMapper).
// At the call site, the C# member names map to the equivalent Map methods. Note that
// JS Map exposes .size (a property) while .delete, .has, .clear, .set, .get are methods —
// Count → size needs MapProperty, the rest use MapMethod.
//
// The indexer (`dict[key]` GET / `dict[key] = value` SET) is NOT a declarative mapping
// — it's special-cased in ExpressionTransformer.TransformElementAccess and
// OperatorHandler.TransformAssignment because the rewrite shape (`map.get(key)` /
// `map.set(key, value)`) doesn't fit the [MapMethod] template form (the C# operation
// is a syntactic indexer, not a named method symbol).

using Metano.Annotations;

// ─── Count → size ───────────────────────────────────────────
// Roslyn binds `IDictionary<K,V>.Count` and `IReadOnlyDictionary<K,V>.Count` to the
// inherited `ICollection<KeyValuePair<K,V>>.Count` / `IReadOnlyCollection<…>.Count`,
// which lower to the array `length`. The extractor rebinds a `Count` read on one of the
// three dictionary types to that dictionary type, and folds `dict.Keys.Count` /
// `dict.Values.Count` into `dict.Count`, so these entries cover every count of a
// dictionary.

[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Count),
    JsProperty = "size",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Count),
    JsProperty = "size",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Count),
    JsProperty = "size",
    DartProperty = "length"
)]

// ─── Keys / Values → array copies ───────────────────────────
// JS `Map.keys()` / `Map.values()` return one-shot iterators, while the C# views can be
// enumerated many times and `IDictionary<K,V>.Keys` is an `ICollection<K>` (a TS array).
// Both views therefore lower to an array copy, matching the array type IrTypeRefMapper
// gives `KeyCollection` / `ValueCollection`. The Dart backend does not render
// `DartTemplate` yet, so in Dart a view stays the `Iterable` `map.keys` / `map.values`
// while a view-typed position is a `List`. Unlike the C# views, the copy does not
// follow later changes to the dictionary. `dict.Keys.Count` and `dict.Keys.Contains(k)`
// never build the copy: the extractor folds them into `dict.Count` and
// `dict.ContainsKey(k)`.

[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Keys),
    JsTemplate = "[...$this.keys()]"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Keys),
    JsTemplate = "[...$this.keys()]"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Keys),
    JsTemplate = "[...$this.keys()]"
)]
[assembly: MapProperty(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Values),
    JsTemplate = "[...$this.values()]"
)]
[assembly: MapProperty(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Values),
    JsTemplate = "[...$this.values()]"
)]
[assembly: MapProperty(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.Values),
    JsTemplate = "[...$this.values()]"
)]

// ─── KeyCollection / ValueCollection → array members ────────
// A view held in a variable (`var keys = dict.Keys;`) is the array copy.

[assembly: MapProperty(
    typeof(Dictionary<,>.KeyCollection),
    nameof(Dictionary<int, int>.KeyCollection.Count),
    JsProperty = "length",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(Dictionary<,>.ValueCollection),
    nameof(Dictionary<int, int>.ValueCollection.Count),
    JsProperty = "length",
    DartProperty = "length"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>.KeyCollection),
    nameof(Dictionary<int, int>.KeyCollection.Contains),
    JsMethod = "includes",
    DartMethod = "contains"
)]

// ─── ContainsKey → has ──────────────────────────────────────

[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.ContainsKey),
    JsMethod = "has"
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.ContainsKey),
    JsMethod = "has"
)]
[assembly: MapMethod(
    typeof(IReadOnlyDictionary<,>),
    nameof(IReadOnlyDictionary<int, int>.ContainsKey),
    JsMethod = "has"
)]

// ─── Add(key, value) → set(key, value) ──────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Add), JsMethod = "set")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Add), JsMethod = "set")]

// ─── Remove → delete ────────────────────────────────────────

[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.Remove),
    JsMethod = "delete"
)]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Remove),
    JsMethod = "delete"
)]

// ─── Clear → clear ──────────────────────────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(IDictionary<,>),
    nameof(IDictionary<int, int>.Clear),
    JsMethod = "clear"
)]

// ─── TryGetValue (intentionally not mapped) ─────────────────
// TryGetValue uses an out-parameter idiom that doesn't translate cleanly to JS Map.get
// (which returns the value or undefined). Lowering it correctly needs a multi-statement
// rewrite that the current declarative pipeline can't express. Stays unmapped — calls
// fall through to the default invocation handler. Tracked as a follow-up.

// ─── GetValueOrDefault → getValueOrDefault(map, key, default) ─
// `GetValueOrDefault` is an extension on IReadOnlyDictionary<K,V> (CollectionExtensions),
// and JS Map has no counterpart. The extractor normalizes the single-argument overload to
// the `(key, defaultValue)` one, passing `default(TValue)` (`0`, `false`, `null`, …), so
// only the two-argument shape reaches this entry. The runtime helper tests membership
// with `has`, so a stored `null` is returned as is instead of being replaced by the
// default.

[assembly: MapMethod(
    typeof(CollectionExtensions),
    nameof(CollectionExtensions.GetValueOrDefault),
    JsTemplate = "getValueOrDefault($this, $0, $1)",
    RuntimeImports = "getValueOrDefault",
    WhenArgCount = 2
)]
