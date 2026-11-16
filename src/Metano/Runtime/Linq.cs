// Declarative BCL → JavaScript mappings for System.Linq.Enumerable.
//
// Every LINQ operator with a runtime counterpart lowers to the pipe form
// `linq(source, where(p), select(s), toArray())` of `metano-runtime/system/linq`, in the
// extension form (`xs.Where(p)`) and the static one (`Enumerable.Where(xs, p)`) alike. That
// lowering is the compiler's chain walker (`IrLinqMapping` and the runtime-signature table
// beside it), not a mapping: nothing here wraps a receiver. A method with no pipe operator
// reports MS0001 and lowers to a throwing placeholder.
//
// What stays declarative is a LINQ generator, which is no stage of a chain.

using Metano.Annotations;

// ─── Generators (eager — materialize to a real JS array) ─
//
// Enumerable.Range(start, count) is a generator, not a chain stage, so it has no
// pipe-runtime counterpart in IrLinqMapping. It lowers to a plain JS array built
// with the Array.from length form — `$0` is the start, `$1` the count. Emitting a
// concrete array (rather than a lazy iterable) is what consumers like SolidJS's
// `<For each={…}>` require, and uses only JS built-ins so no runtime import is added.
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Range),
    JsTemplate = "Array.from({ length: $1 }, (_, i) => $0 + i)"
)]
