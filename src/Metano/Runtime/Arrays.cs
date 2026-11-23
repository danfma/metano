// Declarative BCL → JavaScript mappings for the searches and sorts of System.Array
// (#663, #664). `Array.Sort` and `Array.IndexOf` have no JavaScript namesake, and the
// JavaScript `sort()` orders by text and `indexOf` compares by identity, so each lowers to
// a `metano-runtime` helper with `Comparer<T>.Default` / `EqualityComparer<T>.Default`
// semantics (the same ones the List<T> members use, Lists.cs).
//
// The span extensions of the same names (MemoryExtensions, Spans.cs) lower to the same
// helpers. Overloads with an index range (`Sort(array, index, length)`,
// `IndexOf(array, value, start, count)`) stay unmapped.

using Metano.Annotations;

// ─── Sort ────────────────────────────────────────────────────────
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.Sort),
    JsTemplate = "sortArray($0)",
    RuntimeImports = "sortArray",
    WhenParameterTypeNames = ["T[]"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.Sort),
    JsTemplate = "sortArray($0, $1)",
    RuntimeImports = "sortArray",
    WhenParameterTypeNames = ["T[]", "System.Comparison<T>"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.Sort),
    JsTemplate = "sortArray($0, $1)",
    RuntimeImports = "sortArray",
    WhenParameterTypeNames = ["T[]", "System.Collections.Generic.IComparer<T>"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.Sort),
    JsTemplate = "sortArrayByKeys($0, $1)",
    RuntimeImports = "sortArrayByKeys",
    WhenParameterTypeNames = ["TKey[]", "TValue[]"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.Sort),
    JsTemplate = "sortArrayByKeys($0, $1, $2)",
    RuntimeImports = "sortArrayByKeys",
    WhenParameterTypeNames = ["TKey[]", "TValue[]", "System.Collections.Generic.IComparer<T>"]
)]

// ─── IndexOf / LastIndexOf ───────────────────────────────────────
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.IndexOf),
    JsTemplate = "arrayIndexOf($0, $1)",
    RuntimeImports = "arrayIndexOf",
    WhenParameterTypeNames = ["T[]", "T"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.IndexOf),
    JsTemplate = "arrayIndexOf($0, $1, $2)",
    RuntimeImports = "arrayIndexOf",
    WhenParameterTypeNames = ["T[]", "T", "int"]
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.LastIndexOf),
    JsTemplate = "arrayLastIndexOf($0, $1)",
    RuntimeImports = "arrayLastIndexOf",
    WhenParameterTypeNames = ["T[]", "T"]
)]
