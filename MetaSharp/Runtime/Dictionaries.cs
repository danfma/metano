// Declarative BCL → JavaScript mappings for the System.Collections.Generic dictionary
// family.
//
// Dictionary<K,V> lowers to JS Map<K,V> at the type level (handled by TypeMapper).
// At the call site, the C# member names map to the equivalent Map methods. Note that
// JS Map exposes .size (a property) while .delete, .has, .clear, .set, .get are methods —
// Count → size needs MapProperty, the rest use MapMethod.
//
// IReadOnlyDictionary<K,V> exposes a smaller subset (ContainsKey + indexer + Count). The
// indexer doesn't need a member-level mapping because the property/index access is
// emitted directly.

using System.Collections.Generic;
using MetaSharp.Annotations;

// ─── Count → size ───────────────────────────────────────────

[assembly: MapProperty(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Count), JsProperty = "size")]
[assembly: MapProperty(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Count), JsProperty = "size")]
[assembly: MapProperty(typeof(IReadOnlyDictionary<,>), nameof(IReadOnlyDictionary<int, int>.Count), JsProperty = "size")]

// ─── ContainsKey → has ──────────────────────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.ContainsKey), JsMethod = "has")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.ContainsKey), JsMethod = "has")]
[assembly: MapMethod(typeof(IReadOnlyDictionary<,>), nameof(IReadOnlyDictionary<int, int>.ContainsKey), JsMethod = "has")]

// ─── Add(key, value) → set(key, value) ──────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Add), JsMethod = "set")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Add), JsMethod = "set")]

// ─── Remove → delete ────────────────────────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Remove), JsMethod = "delete")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Remove), JsMethod = "delete")]

// ─── Clear → clear ──────────────────────────────────────────

[assembly: MapMethod(typeof(Dictionary<,>), nameof(Dictionary<int, int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(IDictionary<,>), nameof(IDictionary<int, int>.Clear), JsMethod = "clear")]

// ─── TryGetValue (intentionally not mapped) ─────────────────
// TryGetValue uses an out-parameter idiom that doesn't translate cleanly to JS Map.get
// (which returns the value or undefined). Lowering it correctly needs a multi-statement
// rewrite that the current declarative pipeline can't express. Stays unmapped — calls
// fall through to the default invocation handler. Tracked as a follow-up.
