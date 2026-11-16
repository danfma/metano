// Declarative BCL → JavaScript mappings for the System.Collections.Immutable family.
//
// ImmutableList<T> and ImmutableArray<T> both lower to plain JS arrays at the type
// level (handled by TypeMapper.IsCollectionLike). Every mutation method lowers to a
// call on the `ImmutableCollection` namespace from metano-runtime, which returns
// a NEW array and never mutates the source. This mirrors the C# immutable contract.
//
// The namespace approach (vs. inline spread templates) gives:
// - Readable generated code: `ImmutableCollection.add(list, item)` vs `[...list, item]`
// - Debuggable stack traces (named functions, not anonymous IIFE)
// - Single source of truth in the runtime; swap implementation without touching bindings
// - No wrapper/class overhead; the representation stays as a plain JS array so
//   JSON.stringify works without custom serialization

using System.Collections.Immutable;
using Metano.Annotations;

// ─── ImmutableList<T> ───────────────────────────────────────

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Add),
    JsTemplate = "ImmutableCollection.add($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.AddRange),
    JsTemplate = "ImmutableCollection.addRange($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Insert),
    JsTemplate = "ImmutableCollection.insert($this, $0, $1)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.RemoveAt),
    JsTemplate = "ImmutableCollection.removeAt($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Remove),
    JsTemplate = "ImmutableCollection.remove($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Clear),
    JsTemplate = "ImmutableCollection.clear()",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapProperty(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Empty),
    JsTemplate = "[]"
)]

[assembly: MapProperty(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Count),
    JsProperty = "length"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.IndexOf),
    JsMethod = "indexOf"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Contains),
    JsMethod = "includes"
)]

// ─── ImmutableArray<T> ──────────────────────────────────────

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Add),
    JsTemplate = "ImmutableCollection.add($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.AddRange),
    JsTemplate = "ImmutableCollection.addRange($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Insert),
    JsTemplate = "ImmutableCollection.insert($this, $0, $1)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.RemoveAt),
    JsTemplate = "ImmutableCollection.removeAt($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Remove),
    JsTemplate = "ImmutableCollection.remove($this, $0)",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Clear),
    JsTemplate = "ImmutableCollection.clear()",
    RuntimeImports = "ImmutableCollection"
)]

[assembly: MapProperty(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Empty),
    JsTemplate = "[]"
)]

[assembly: MapProperty(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Length),
    JsProperty = "length"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Contains),
    JsMethod = "includes"
)]
