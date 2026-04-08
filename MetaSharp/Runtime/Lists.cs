// Declarative BCL → JavaScript mappings for List<T> and the ICollection / IList family.
// Read at compile time by the MetaSharp transpiler via DeclarativeMappingRegistry.
//
// As more mappings move from the hardcoded BclMapper into this folder, the corresponding
// branches in BclMapper.cs are deleted. End state: BclMapper is a pure dispatcher over
// the declarative registry and this folder owns all of the BCL → JS lowering.

using System.Collections.Generic;
using MetaSharp.Annotations;

// List<T>.Count → list.length
[assembly: MapProperty(typeof(List<>), "Count", JsProperty = "length")]

// list.Add(x) → list.push(x)
[assembly: MapMethod(typeof(List<>), "Add", JsMethod = "push")]

// list.AddRange(other) → list.push(...other) — JS doesn't have a single addRange,
// so we splat the source via the spread operator. Demonstrates the JsTemplate form
// (no hardcoded equivalent existed in BclMapper before declarative mappings landed).
[assembly: MapMethod(typeof(List<>), "AddRange", JsTemplate = "$this.push(...$0)")]
