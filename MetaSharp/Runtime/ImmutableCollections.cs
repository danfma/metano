// Declarative BCL → JavaScript mappings for the System.Collections.Immutable family.
//
// ImmutableList<T> and ImmutableArray<T> both lower to plain JS arrays at the type level
// (handled by TypeMapper.IsCollectionLike). The non-mutating Add / Remove / Insert /
// Clear methods become spread expressions that return a NEW array, mirroring the
// immutable contract — `var newList = old.Add(x);` lowers to
// `const newList = [...old, x];` and the original array is left untouched.
//
// Note about the receiver placeholder ($this): for templates that reference $this
// multiple times we wrap the body in an IIFE so the receiver expression is only
// evaluated once. Otherwise, when the C# receiver is a method call (e.g., `GetItems()`),
// each $this substitution would re-execute the call and the immutable methods would
// operate on different snapshots.

using System.Collections.Immutable;
using MetaSharp.Annotations;

// ─── ImmutableList<T> ───────────────────────────────────────

// list.Add(item) → [...list, item]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Add),
    JsTemplate = "[...$this, $0]")]

// list.AddRange(other) → [...list, ...other]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.AddRange),
    JsTemplate = "[...$this, ...$0]")]

// list.Insert(index, item) → captured-receiver IIFE that splices in the new item.
// We use `with`-style spread instead of mutating splice to preserve immutability.
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Insert),
    JsTemplate = "((arr) => [...arr.slice(0, $0), $1, ...arr.slice($0)])($this)")]

// list.RemoveAt(index) → captured-receiver IIFE that drops the index'th element.
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.RemoveAt),
    JsTemplate = "((arr) => [...arr.slice(0, $0), ...arr.slice($0 + 1)])($this)")]

// list.Remove(item) → captured-receiver IIFE: find the index, drop the element if found,
// otherwise return the original array unchanged (matching ImmutableList<T>.Remove).
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Remove),
    JsTemplate = "((arr) => { const i = arr.indexOf($0); return i >= 0 ? [...arr.slice(0, i), ...arr.slice(i + 1)] : arr; })($this)")]

// list.Clear() → []
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Clear),
    JsTemplate = "[]")]

// ImmutableList<T>.Empty (static property) → [] — the receiver (the type identifier)
// is dropped because the template doesn't reference $this.
[assembly: MapProperty(typeof(ImmutableList<>), nameof(ImmutableList<int>.Empty),
    JsTemplate = "[]")]

// list.Count and IndexOf reuse the JS array shape directly.
[assembly: MapProperty(typeof(ImmutableList<>), nameof(ImmutableList<int>.Count),
    JsProperty = "length")]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.IndexOf),
    JsMethod = "indexOf")]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Contains),
    JsMethod = "includes")]

// ─── ImmutableArray<T> ──────────────────────────────────────
// Same shape as ImmutableList<T> at the JS level, just a different declaring type so
// each declaration has to repeat. (Could be deduplicated if [MapMethod] grew a
// "DeclaringTypes" array property — out of scope for now.)

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Add),
    JsTemplate = "[...$this, $0]")]

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.AddRange),
    JsTemplate = "[...$this, ...$0]")]

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Insert),
    JsTemplate = "((arr) => [...arr.slice(0, $0), $1, ...arr.slice($0)])($this)")]

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.RemoveAt),
    JsTemplate = "((arr) => [...arr.slice(0, $0), ...arr.slice($0 + 1)])($this)")]

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Remove),
    JsTemplate = "((arr) => { const i = arr.indexOf($0); return i >= 0 ? [...arr.slice(0, i), ...arr.slice(i + 1)] : arr; })($this)")]

[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Clear),
    JsTemplate = "[]")]

// ImmutableArray<T>.Empty (static field) → []
[assembly: MapProperty(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Empty),
    JsTemplate = "[]")]

[assembly: MapProperty(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Length),
    JsProperty = "length")]
[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf")]
[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Contains),
    JsMethod = "includes")]
