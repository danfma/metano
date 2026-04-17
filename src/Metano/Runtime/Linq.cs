// Declarative BCL → JavaScript mappings for the System.Linq.Enumerable extension methods.
//
// Every LINQ call lowers to a method on the metano-runtime Enumerable wrapper. The
// runtime is lazy: composition methods (Where, Select, OrderBy, …) return a wrapper, and
// only the terminal methods (ToArray, First, Sum, …) materialize the chain.
//
// All declarations use `WrapReceiver = "Enumerable.from"` so a call like `arr.Where(p)`
// lowers to `Enumerable.from(arr).where(p)`. The BclMapper detects already-wrapped
// receivers in two ways: (1) the receiver is a call into Enumerable.* (e.g., the wrap
// itself or Enumerable.range/empty/etc.), and (2) the receiver is a fluent chain whose
// method name is one of the JS names listed below. Long chains therefore wrap only the
// very first call:
//
//     arr.Where(p).Select(s).ToList()
//
// becomes
//
//     Enumerable.from(arr).where(p).select(s).toArray()
//
// — the .select() and .toArray() steps see a chained receiver and skip re-wrapping.

using Metano.Annotations;

// ─── Composition (lazy — returns the runtime Enumerable wrapper) ─

[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Where),
    WrapReceiver = "Enumerable.from",
    JsMethod = "where"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Select),
    WrapReceiver = "Enumerable.from",
    JsMethod = "select"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.SelectMany),
    WrapReceiver = "Enumerable.from",
    JsMethod = "selectMany"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.OrderBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "orderBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.OrderByDescending),
    WrapReceiver = "Enumerable.from",
    JsMethod = "orderByDescending"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ThenBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "thenBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ThenByDescending),
    WrapReceiver = "Enumerable.from",
    JsMethod = "thenByDescending"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Take),
    WrapReceiver = "Enumerable.from",
    JsMethod = "take"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Skip),
    WrapReceiver = "Enumerable.from",
    JsMethod = "skip"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Distinct),
    WrapReceiver = "Enumerable.from",
    JsMethod = "distinct"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.GroupBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "groupBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Concat),
    WrapReceiver = "Enumerable.from",
    JsMethod = "concat"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.TakeWhile),
    WrapReceiver = "Enumerable.from",
    JsMethod = "takeWhile"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.SkipWhile),
    WrapReceiver = "Enumerable.from",
    JsMethod = "skipWhile"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.DistinctBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "distinctBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Reverse),
    WrapReceiver = "Enumerable.from",
    JsMethod = "reverse"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Zip),
    WrapReceiver = "Enumerable.from",
    JsMethod = "zip"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Append),
    WrapReceiver = "Enumerable.from",
    JsMethod = "append"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Prepend),
    WrapReceiver = "Enumerable.from",
    JsMethod = "prepend"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Union),
    WrapReceiver = "Enumerable.from",
    JsMethod = "union"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Intersect),
    WrapReceiver = "Enumerable.from",
    JsMethod = "intersect"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Except),
    WrapReceiver = "Enumerable.from",
    JsMethod = "except"
)]

// ─── Terminal (materializes the chain) ───────────────────────────

// Both ToList and ToArray map to toArray (the runtime materializes into a JS array).
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ToList),
    WrapReceiver = "Enumerable.from",
    JsMethod = "toArray"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ToArray),
    WrapReceiver = "Enumerable.from",
    JsMethod = "toArray"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ToDictionary),
    WrapReceiver = "Enumerable.from",
    JsMethod = "toMap"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.ToHashSet),
    WrapReceiver = "Enumerable.from",
    JsMethod = "toSet"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.First),
    WrapReceiver = "Enumerable.from",
    JsMethod = "first"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.FirstOrDefault),
    WrapReceiver = "Enumerable.from",
    JsMethod = "firstOrDefault"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Last),
    WrapReceiver = "Enumerable.from",
    JsMethod = "last"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.LastOrDefault),
    WrapReceiver = "Enumerable.from",
    JsMethod = "lastOrDefault"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Single),
    WrapReceiver = "Enumerable.from",
    JsMethod = "single"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.SingleOrDefault),
    WrapReceiver = "Enumerable.from",
    JsMethod = "singleOrDefault"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Any),
    WrapReceiver = "Enumerable.from",
    JsMethod = "any"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.All),
    WrapReceiver = "Enumerable.from",
    JsMethod = "all"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Count),
    WrapReceiver = "Enumerable.from",
    JsMethod = "count"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Sum),
    WrapReceiver = "Enumerable.from",
    JsMethod = "sum"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Average),
    WrapReceiver = "Enumerable.from",
    JsMethod = "average"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Min),
    WrapReceiver = "Enumerable.from",
    JsMethod = "min"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Max),
    WrapReceiver = "Enumerable.from",
    JsMethod = "max"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.MinBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "minBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.MaxBy),
    WrapReceiver = "Enumerable.from",
    JsMethod = "maxBy"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Contains),
    WrapReceiver = "Enumerable.from",
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Aggregate),
    WrapReceiver = "Enumerable.from",
    JsMethod = "aggregate"
)]
