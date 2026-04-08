// Declarative BCL → JavaScript mappings for the System.Collections.Generic List family.
// Read at compile time by the MetaSharp transpiler via DeclarativeMappingRegistry.
//
// Each interface variant is declared explicitly because Roslyn resolves a method call
// against the symbol's most-specific containing type, and the registry compares against
// that type's OriginalDefinition. So `someList.Add(x)` where someList: List<int> binds
// to `List<T>.Add`, while `someCollection.Add(x)` where someCollection: ICollection<int>
// binds to `ICollection<T>.Add` — both must be declared if both call shapes need to lower.
//
// The C# member name is captured via `nameof(Type.Member)` rather than a string literal
// so typos surface at compile time. For generic types we need a closed instantiation
// (e.g., `nameof(List<int>.Add)`); the int is arbitrary because nameof only captures the
// simple name.
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

[assembly: MapProperty(typeof(List<>), nameof(List<int>.Count), JsProperty = "length")]
[assembly: MapProperty(typeof(IList<>), nameof(IList<int>.Count), JsProperty = "length")]
[assembly: MapProperty(typeof(ICollection<>), nameof(ICollection<int>.Count), JsProperty = "length")]
[assembly: MapProperty(typeof(IReadOnlyList<>), nameof(IReadOnlyList<int>.Count), JsProperty = "length")]
[assembly: MapProperty(typeof(IReadOnlyCollection<>), nameof(IReadOnlyCollection<int>.Count), JsProperty = "length")]

// ─── Add ────────────────────────────────────────────────────
// Add exists on the mutable interfaces. IReadOnlyList<T> and IReadOnlyCollection<T> do
// not declare Add, so we don't map them — calls through those types fall through to
// LINQ extensions or simply fail to bind in C# in the first place.

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Add), JsMethod = "push")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Add), JsMethod = "push")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Add), JsMethod = "push")]

// list.AddRange(other) → list.push(...other)
// JS has no addRange; we splat the source via the spread operator. Demonstrates the
// JsTemplate form (no hardcoded equivalent existed in BclMapper before declarative
// mappings landed).
[assembly: MapMethod(typeof(List<>), nameof(List<int>.AddRange), JsTemplate = "$this.push(...$0)")]

// ─── Contains / IndexOf ─────────────────────────────────────
// Contains is on ICollection<T> (and inherited by IList<T>, List<T>). The read-only
// interfaces inherit Contains via the System.Linq Enumerable extension instead, which
// the LINQ branch of BclMapper handles separately.

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Contains), JsMethod = "includes")]

// IndexOf is on IList<T> (and inherited by List<T>). IReadOnlyList<T> does NOT declare
// IndexOf — calls through that type go through the LINQ Enumerable.IndexOf extension
// instead, which the LINQ branch of BclMapper handles separately. (The previous
// hardcoded mapping had a dead `typeof(IReadOnlyList<>), "IndexOf"` entry caught by
// the nameof() validation.)
[assembly: MapMethod(typeof(List<>), nameof(List<int>.IndexOf), JsMethod = "indexOf")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.IndexOf), JsMethod = "indexOf")]

// ─── Insert ─────────────────────────────────────────────────
// list.Insert(index, item) → list.splice(index, 0, item)
// Note: the previous hardcoded mapping was `"Insert" => "splice"` which dropped the
// deleteCount argument and produced a buggy `list.splice(index, item)` call. The
// template form here is correct.

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Insert), JsTemplate = "$this.splice($0, 0, $1)")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Insert), JsTemplate = "$this.splice($0, 0, $1)")]

// ─── Clear ──────────────────────────────────────────────────
// list.Clear() → list.length = 0
// JS has no Array.clear(); the idiomatic in-place clear is the length-assignment trick.

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Clear), JsTemplate = "$this.length = 0")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Clear), JsTemplate = "$this.length = 0")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Clear), JsTemplate = "$this.length = 0")]

// ─── List<T>-only methods ───────────────────────────────────
// Reverse, Sort, ToArray are concrete methods on List<T>; the interfaces don't declare
// them. JS sort/reverse mutate in place and return the same array (matching List<T>
// semantics), and slice() with no args returns a shallow copy (matching ToArray()).

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Reverse), JsMethod = "reverse")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Sort), JsMethod = "sort")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.ToArray), JsMethod = "slice")]

// ─── Remove ─────────────────────────────────────────────────
// list.Remove(item) → IIFE that finds the index, splices it out if found, and returns
// the boolean directly. Capturing the receiver as the IIFE argument `arr` avoids
// double-evaluation when the C# receiver is a method call instead of a plain identifier.

[assembly: MapMethod(typeof(List<>), nameof(List<int>.Remove),
    JsTemplate = "((arr) => { const i = arr.indexOf($0); if (i >= 0) { arr.splice(i, 1); return true; } return false; })($this)")]
