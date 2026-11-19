// Declarative mappings for the explicit crossings between a runtime List<T> and the native
// JavaScript array (spec 011 FR-004, FR-055, FR-056, stage 011c). Every crossing is written
// in C#; the compiler never inserts one. The views lower to the free helpers of
// metano-runtime/system/collections (each tree-shakes on its own and handles null), the
// copies to `toArray()`, and JsArray<T>'s own members to the Array members of the same
// name (camelCase, no mapping needed); only the members whose JavaScript spelling differs
// are listed here.

using System.Runtime.InteropServices;
using Metano.Annotations;
using Metano.Collections;
using Metano.Interop;

// ─── Views (zero copy, FR-056) ─────────────────────────────────────
[assembly: MapMethod(
    typeof(InteropExtensions),
    nameof(InteropExtensions.AsJsArray),
    JsTemplate = "asJsArray($this)",
    RuntimeImports = "asJsArray"
)]
[assembly: MapMethod(
    typeof(ListViewExtensions),
    nameof(ListViewExtensions.AsSpan),
    JsTemplate = "asSpan($this)",
    RuntimeImports = "asSpan"
)]
[assembly: MapMethod(
    typeof(ListViewExtensions),
    nameof(ListViewExtensions.AsReadOnlySpan),
    JsTemplate = "asReadOnlySpan($this)",
    RuntimeImports = "asReadOnlySpan"
)]
[assembly: MapMethod(
    typeof(CollectionsMarshal),
    nameof(CollectionsMarshal.AsSpan),
    JsTemplate = "asSpan($0)",
    RuntimeImports = "asSpan"
)]

// ─── Copies (FR-055) ───────────────────────────────────────────────
[assembly: MapMethod(
    typeof(InteropExtensions),
    nameof(InteropExtensions.ToJsArray),
    JsTemplate = "$this.toArray()"
)]
[assembly: MapMethod(typeof(JsArray), nameof(JsArray.From), JsTemplate = "Array.from($0)")]

// ─── JsArray<T> → List<T> (shared storage) ─────────────────────────
[assembly: MapMethod(
    typeof(JsArray<>),
    nameof(JsArray<int>.AsList),
    JsTemplate = "List.adopt($this)",
    RuntimeImports = "List"
)]
[assembly: MapProperty(typeof(JsArray<>), nameof(JsArray<int>.Length), JsProperty = "length")]

// ─── new JsArray<T>() / new JsArray<T>(items) ──────────────────────
[assembly: MapMethod(
    typeof(JsArray<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[]",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(JsArray<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Array.from($0)",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
