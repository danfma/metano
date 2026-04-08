// Declarative BCL → JavaScript mappings for the System.Collections.Generic List family.
// Read at compile time by the MetaSharp transpiler via DeclarativeMappingRegistry.
//
// Each interface variant is declared explicitly because Roslyn resolves a method call
// against the symbol's most-specific containing type, and the registry compares against
// that type's OriginalDefinition. So `someList.Add(x)` where someList: List<int> binds
// to `List<T>.Add`, while `someCollection.Add(x)` where someCollection: ICollection<int>
// binds to `ICollection<T>.Add` — both must be declared if both call shapes need to lower.
//
// ImmutableList<T> / ImmutableArray<T> are intentionally NOT mapped here. The previous
// hardcoded BclMapper mapped Immutable.Add → push, which is silently broken because
// immutable Add returns a new collection instead of mutating in place. Adding declarative
// support for the immutable family requires a different lowering strategy and is tracked
// as a follow-up.

using System.Collections.Generic;
using MetaSharp.Annotations;

// ─── Count ──────────────────────────────────────────────────
// All five collection interfaces expose Count. JS uses .length on arrays, which is what
// the runtime representation of every transpiled collection ultimately is.

[assembly: MapProperty(typeof(List<>), "Count", JsProperty = "length")]
[assembly: MapProperty(typeof(IList<>), "Count", JsProperty = "length")]
[assembly: MapProperty(typeof(ICollection<>), "Count", JsProperty = "length")]
[assembly: MapProperty(typeof(IReadOnlyList<>), "Count", JsProperty = "length")]
[assembly: MapProperty(typeof(IReadOnlyCollection<>), "Count", JsProperty = "length")]

// ─── Add ────────────────────────────────────────────────────
// Add exists on the mutable interfaces. IReadOnlyList<T> and IReadOnlyCollection<T> do
// not declare Add, so we don't map them — calls through those types fall through to
// LINQ extensions or simply fail to bind in C# in the first place.

[assembly: MapMethod(typeof(List<>), "Add", JsMethod = "push")]
[assembly: MapMethod(typeof(IList<>), "Add", JsMethod = "push")]
[assembly: MapMethod(typeof(ICollection<>), "Add", JsMethod = "push")]

// list.AddRange(other) → list.push(...other)
// JS has no addRange; we splat the source via the spread operator. Demonstrates the
// JsTemplate form (no hardcoded equivalent existed in BclMapper before declarative
// mappings landed).
[assembly: MapMethod(typeof(List<>), "AddRange", JsTemplate = "$this.push(...$0)")]

// ─── Contains / IndexOf ─────────────────────────────────────
// Contains is on ICollection<T> (and inherited by IList<T>, List<T>). The read-only
// interfaces inherit Contains via the System.Linq Enumerable extension instead, which
// the LINQ branch of BclMapper handles separately.

[assembly: MapMethod(typeof(List<>), "Contains", JsMethod = "includes")]
[assembly: MapMethod(typeof(IList<>), "Contains", JsMethod = "includes")]
[assembly: MapMethod(typeof(ICollection<>), "Contains", JsMethod = "includes")]

// IndexOf is on IList<T> (and IReadOnlyList<T>); both inherit it on List<T>.
[assembly: MapMethod(typeof(List<>), "IndexOf", JsMethod = "indexOf")]
[assembly: MapMethod(typeof(IList<>), "IndexOf", JsMethod = "indexOf")]
[assembly: MapMethod(typeof(IReadOnlyList<>), "IndexOf", JsMethod = "indexOf")]

// ─── Insert ─────────────────────────────────────────────────
// list.Insert(index, item) → list.splice(index, 0, item)
// Note: the previous hardcoded mapping was `"Insert" => "splice"` which dropped the
// deleteCount argument and produced a buggy `list.splice(index, item)` call. The
// template form here is correct.

[assembly: MapMethod(typeof(List<>), "Insert", JsTemplate = "$this.splice($0, 0, $1)")]
[assembly: MapMethod(typeof(IList<>), "Insert", JsTemplate = "$this.splice($0, 0, $1)")]

// ─── Clear ──────────────────────────────────────────────────
// list.Clear() → list.length = 0
// JS has no Array.clear(); the idiomatic in-place clear is the length-assignment trick.

[assembly: MapMethod(typeof(List<>), "Clear", JsTemplate = "$this.length = 0")]
[assembly: MapMethod(typeof(IList<>), "Clear", JsTemplate = "$this.length = 0")]
[assembly: MapMethod(typeof(ICollection<>), "Clear", JsTemplate = "$this.length = 0")]

// ─── List<T>-only methods ───────────────────────────────────
// Reverse, Sort, ToArray are concrete methods on List<T>; the interfaces don't declare
// them. JS sort/reverse mutate in place and return the same array (matching List<T>
// semantics), and slice() with no args returns a shallow copy (matching ToArray()).

[assembly: MapMethod(typeof(List<>), "Reverse", JsMethod = "reverse")]
[assembly: MapMethod(typeof(List<>), "Sort", JsMethod = "sort")]
[assembly: MapMethod(typeof(List<>), "ToArray", JsMethod = "slice")]

// ─── Remove (intentionally not mapped) ──────────────────────
// List<T>.Remove(item) returns a bool (true if found and removed). A naive splice-based
// template would break that contract because splice returns the removed elements as an
// array (truthy when empty, BUG). Leaving this unmapped preserves the previous BclMapper
// behavior of returning null (no special lowering) and is tracked as a follow-up.
