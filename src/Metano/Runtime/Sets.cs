// Declarative BCL → JavaScript mappings for the System.Collections.Generic set family.
//
// HashSet<T> lowers to a custom HashSet implementation in metano-runtime that uses
// equals/hashCode for value-based equality (mirroring C# semantics). The runtime API
// exposes the same surface as the JS Set built-in (add, delete, has, clear, size) plus
// extra hooks for the equality contract.

using Metano.Annotations;

// ─── Count → size ───────────────────────────────────────────

[assembly: MapProperty(typeof(HashSet<>), nameof(HashSet<int>.Count), JsProperty = "size")]
[assembly: MapProperty(typeof(ISet<>), nameof(ISet<int>.Count), JsProperty = "size")]
[assembly: MapProperty(typeof(SortedSet<>), nameof(SortedSet<int>.Count), JsProperty = "size")]

// ─── Add → add (the JS Set member name happens to match) ────

[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Add), JsMethod = "add")]

// ─── Contains → has ─────────────────────────────────────────

[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Contains), JsMethod = "has")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Contains), JsMethod = "has")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Contains), JsMethod = "has")]

// ─── Remove → delete ────────────────────────────────────────

[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Remove), JsMethod = "delete")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Remove), JsMethod = "delete")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Remove), JsMethod = "delete")]

// ─── Clear → clear ──────────────────────────────────────────

[assembly: MapMethod(typeof(HashSet<>), nameof(HashSet<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(ISet<>), nameof(ISet<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(SortedSet<>), nameof(SortedSet<int>.Clear), JsMethod = "clear")]

// ─── Constructors ───────────────────────────────────────────
// new HashSet<T>() / new HashSet<T>(capacity) → new HashSet<T>() (the capacity is a
// preallocation hint, dropped); new HashSet<T>(collection) → new HashSet<T>(collection).
// The comparer overloads stay unmapped: the runtime set compares through the
// equals/hashCode contract and takes no comparer.

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
    JsTemplate = "new HashSet<$T0>()",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new HashSet<$T0>($0)",
    RuntimeImports = "HashSet",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
