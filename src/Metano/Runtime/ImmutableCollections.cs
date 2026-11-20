// Declarative BCL → JavaScript mappings for System.Collections.Immutable (spec 011 FR-054,
// OD-17, stage 011h): `ImmutableList<T>`, `ImmutableArray<T>` (a sibling class over one
// shared core), `ImmutableHashSet<T>`, `ImmutableDictionary<K, V>`, their builders and the
// `IImmutable*` interfaces are the `metano-runtime` classes and interfaces of the same
// names (contracts/runtime-collections.md § 011h classes). Instance members are plain
// renames to the camelCase runtime member; the indexers are the element-access bridge's
// `get` (the immutable lists' indexer is read-only), and the members the runtime does not
// implement (`ItemRef`, `IsDefault`, `AsSpan`, the index/count overloads, …) have no
// mapping and report MS0001.
//
// The factories follow the contract's of/from rule: `of` takes ownership of a fresh array,
// so it receives only the literal C# builds for an expanded `params` call or the element
// overloads (`Create(a, b)`), which the extractor gathers into one array argument
// (IntrinsicBclLoweringTable.ImmutableFactories); a `Create` passed an existing array or
// span in normal form is `CreateRange` there, a copy through `from`. A leading C#
// comparer moves after the source, so the two evaluate in the TypeScript order. The
// builders are composition over the mutable classes: `ToBuilder()` / `CreateBuilder()`
// construct one, and `ToImmutable()` is the free `toImmutable*` helper of its module, so
// an immutable class never imports its builder. `ImmutableList<T>.Empty` (and the other
// immutables' `Empty`) is read as the parameterless `Create<T>()`, which carries the type
// arguments (IrExpressionExtractor.ImmutableEmpty).

using System.Collections.Immutable;
using Metano.Annotations;

// ─── ImmutableList<T> ────────────────────────────────────────
[assembly: MapProperty(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.IsEmpty),
    JsProperty = "isEmpty"
)]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Add), JsMethod = "add")]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.AddRange),
    JsMethod = "addRange"
)]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.ConvertAll),
    JsMethod = "convertAll"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Exists),
    JsMethod = "exists"
)]
[assembly: MapMethod(typeof(ImmutableList<>), nameof(ImmutableList<int>.Find), JsMethod = "find")]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.FindAll),
    JsMethod = "findAll"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.FindLast),
    JsMethod = "findLast"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.ForEach),
    JsMethod = "forEach"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.GetRange),
    JsMethod = "getRange"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.IndexOf),
    JsMethod = "indexOf"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Insert),
    JsMethod = "insert"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.InsertRange),
    JsMethod = "insertRange"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.LastIndexOf),
    JsMethod = "lastIndexOf"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.RemoveAll),
    JsMethod = "removeAll"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Replace),
    JsMethod = "replace"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.SetItem),
    JsMethod = "setItem"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.TrueForAll),
    JsMethod = "trueForAll"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.BinarySearch),
    JsMethod = "binarySearch",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.CopyTo),
    JsMethod = "copyTo",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.BinarySearch),
    JsMethod = "binarySearch",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.CopyTo),
    JsMethod = "copyTo",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.FindIndex),
    JsMethod = "findIndex",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.FindLastIndex),
    JsMethod = "findLastIndex",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.RemoveRange),
    JsMethod = "removeRange",
    WhenParameterTypes = new[] { typeof(int), typeof(int) }
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.RemoveRange),
    JsMethod = "removeItems"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Reverse),
    JsMethod = "reverse",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Sort),
    JsMethod = "sort",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.Sort),
    JsMethod = "sort",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>),
    nameof(ImmutableList<int>.ToBuilder),
    JsTemplate = "new ImmutableListBuilder($this)",
    RuntimeImports = "ImmutableListBuilder"
)]

// ─── ImmutableArray<T> ───────────────────────────────────────
[assembly: MapProperty(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Length),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IsEmpty),
    JsProperty = "isEmpty"
)]
[assembly: MapMethod(typeof(ImmutableArray<>), nameof(ImmutableArray<int>.Add), JsMethod = "add")]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Insert),
    JsMethod = "insert"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.InsertRange),
    JsMethod = "insertRange"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.LastIndexOf),
    JsMethod = "lastIndexOf"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.RemoveAll),
    JsMethod = "removeAll"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Replace),
    JsMethod = "replace"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.SetItem),
    JsMethod = "setItem"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.AddRange),
    JsMethod = "addRange",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Contains),
    JsMethod = "contains",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]", "int" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.IndexOf),
    JsMethod = "indexOf",
    WhenParameterTypeNames = new[] { "T", "int", "int" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.RemoveRange),
    JsMethod = "removeRange",
    WhenParameterTypes = new[] { typeof(int), typeof(int) }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.RemoveRange),
    JsMethod = "removeItems"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Sort),
    JsMethod = "sort",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.Sort),
    JsMethod = "sort",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>),
    nameof(ImmutableArray<int>.ToBuilder),
    JsTemplate = "new ImmutableArrayBuilder($this)",
    RuntimeImports = "ImmutableArrayBuilder"
)]

// ─── ImmutableHashSet<T> ─────────────────────────────────────
[assembly: MapProperty(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.IsEmpty),
    JsProperty = "isEmpty"
)]
[assembly: MapProperty(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.KeyComparer),
    JsProperty = "keyComparer"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Except),
    JsMethod = "except"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Intersect),
    JsMethod = "intersect"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.IsSubsetOf),
    JsMethod = "isSubsetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Overlaps),
    JsMethod = "overlaps"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.SetEquals),
    JsMethod = "setEquals"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.SymmetricExcept),
    JsMethod = "symmetricExcept"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.Union),
    JsMethod = "union"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.WithComparer),
    JsMethod = "withComparer"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>),
    nameof(ImmutableHashSet<int>.ToBuilder),
    JsTemplate = "new ImmutableHashSetBuilder($this)",
    RuntimeImports = "ImmutableHashSetBuilder"
)]

// ─── ImmutableDictionary<K, V> ──────────────────────────────
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.IsEmpty),
    JsProperty = "isEmpty"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.KeyComparer),
    JsProperty = "keyComparer"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.ValueComparer),
    JsProperty = "valueComparer"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.AddRange),
    JsMethod = "addRange"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.ContainsValue),
    JsMethod = "containsValue"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.RemoveRange),
    JsMethod = "removeRange"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.SetItem),
    JsMethod = "setItem"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.SetItems),
    JsMethod = "setItems"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.TryGetKey),
    JsMethod = "tryGetKey"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.WithComparers),
    JsMethod = "withComparers"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    "GetValueOrDefault",
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>),
    nameof(ImmutableDictionary<int, int>.ToBuilder),
    JsTemplate = "new ImmutableDictionaryBuilder($this)",
    RuntimeImports = "ImmutableDictionaryBuilder"
)]

// ─── Builders ────────────────────────────────────────────────
[assembly: MapProperty(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Count),
    JsProperty = "count"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Insert),
    JsMethod = "insert"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.InsertRange),
    JsMethod = "insertRange"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.RemoveAll),
    JsMethod = "removeAll"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.AddRange),
    JsMethod = "addRange",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Remove),
    JsMethod = "remove",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Reverse),
    JsMethod = "reverse",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Sort),
    JsMethod = "sort",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.Sort),
    JsMethod = "sort",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]" }
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]", "int" }
)]
[assembly: MapMethod(
    typeof(ImmutableList<>.Builder),
    nameof(ImmutableList<int>.Builder.ToImmutable),
    JsTemplate = "toImmutableList($this)",
    RuntimeImports = "toImmutableList"
)]
[assembly: MapProperty(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Count),
    JsProperty = "count"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Insert),
    JsMethod = "insert"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.InsertRange),
    JsMethod = "insertRange"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.RemoveAll),
    JsMethod = "removeAll"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.AddRange),
    JsMethod = "addRange",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Remove),
    JsMethod = "remove",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Reverse),
    JsMethod = "reverse",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Sort),
    JsMethod = "sort",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.Sort),
    JsMethod = "sort",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.CopyTo),
    JsMethod = "copyTo",
    WhenParameterTypeNames = new[] { "T[]", "int" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray<>.Builder),
    nameof(ImmutableArray<int>.Builder.ToImmutable),
    JsTemplate = "toImmutableArray($this)",
    RuntimeImports = "toImmutableArray"
)]
[assembly: MapProperty(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.KeyComparer),
    JsProperty = "keyComparer"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.ExceptWith),
    JsMethod = "exceptWith"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.IntersectWith),
    JsMethod = "intersectWith"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.IsSubsetOf),
    JsMethod = "isSubsetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Overlaps),
    JsMethod = "overlaps"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.SetEquals),
    JsMethod = "setEquals"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.SymmetricExceptWith),
    JsMethod = "symmetricExceptWith"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.UnionWith),
    JsMethod = "unionWith"
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet<>.Builder),
    nameof(ImmutableHashSet<int>.Builder.ToImmutable),
    JsTemplate = "toImmutableHashSet($this)",
    RuntimeImports = "toImmutableHashSet"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.KeyComparer),
    JsProperty = "keyComparer"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.ValueComparer),
    JsProperty = "valueComparer"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Values),
    JsProperty = "values"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.ContainsValue),
    JsMethod = "containsValue"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Add),
    JsMethod = "add",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.GetValueOrDefault),
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.Remove),
    JsMethod = "remove",
    WhenParameterTypeNames = new[] { "TKey" }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary<,>.Builder),
    nameof(ImmutableDictionary<int, int>.Builder.ToImmutable),
    JsTemplate = "toImmutableDictionary($this)",
    RuntimeImports = "toImmutableDictionary"
)]

// ─── IImmutableList<T> / IImmutableSet<T> / IImmutableDictionary<K, V> ─
[assembly: MapMethod(typeof(IImmutableList<>), nameof(IImmutableList<int>.Add), JsMethod = "add")]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.AddRange),
    JsMethod = "addRange"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.IndexOf),
    JsMethod = "indexOf"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.Insert),
    JsMethod = "insert"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.InsertRange),
    JsMethod = "insertRange"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.LastIndexOf),
    JsMethod = "lastIndexOf"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.RemoveAll),
    JsMethod = "removeAll"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.RemoveAt),
    JsMethod = "removeAt"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.Replace),
    JsMethod = "replace"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.SetItem),
    JsMethod = "setItem"
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.RemoveRange),
    JsMethod = "removeRange",
    WhenParameterTypes = new[] { typeof(int), typeof(int) }
)]
[assembly: MapMethod(
    typeof(IImmutableList<>),
    nameof(IImmutableList<int>.RemoveRange),
    JsMethod = "removeItems"
)]
[assembly: MapMethod(typeof(IImmutableSet<>), nameof(IImmutableSet<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(IImmutableSet<>), nameof(IImmutableSet<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.Except),
    JsMethod = "except"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.Intersect),
    JsMethod = "intersect"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.IsProperSubsetOf),
    JsMethod = "isProperSubsetOf"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.IsProperSupersetOf),
    JsMethod = "isProperSupersetOf"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.IsSubsetOf),
    JsMethod = "isSubsetOf"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.IsSupersetOf),
    JsMethod = "isSupersetOf"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.Overlaps),
    JsMethod = "overlaps"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.SetEquals),
    JsMethod = "setEquals"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.SymmetricExcept),
    JsMethod = "symmetricExcept"
)]
[assembly: MapMethod(
    typeof(IImmutableSet<>),
    nameof(IImmutableSet<int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(typeof(IImmutableSet<>), nameof(IImmutableSet<int>.Union), JsMethod = "union")]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.Add),
    JsMethod = "add"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.AddRange),
    JsMethod = "addRange"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.Remove),
    JsMethod = "remove"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.RemoveRange),
    JsMethod = "removeRange"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.SetItem),
    JsMethod = "setItem"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.SetItems),
    JsMethod = "setItems"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    nameof(IImmutableDictionary<int, int>.TryGetKey),
    JsMethod = "tryGetKey"
)]
[assembly: MapMethod(
    typeof(IImmutableDictionary<,>),
    "GetValueOrDefault",
    JsTemplate = "$this.tryGetValue($0, $1)[1]",
    WhenArgCount = 2
)]

// ─── Factories and extension methods ────────────────────────
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.Create),
    JsTemplate = "ImmutableList.empty<$T0>()",
    RuntimeImports = "ImmutableList",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.Create),
    JsTemplate = "ImmutableList.of<$T0>($0)",
    RuntimeImports = "ImmutableList",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.CreateRange),
    JsTemplate = "ImmutableList.from<$T0>($0)",
    RuntimeImports = "ImmutableList"
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.CreateBuilder),
    JsTemplate = "new ImmutableListBuilder<$T0>()",
    RuntimeImports = "ImmutableListBuilder"
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.ToImmutableList),
    JsTemplate = "ImmutableList.from<$T0>($this)",
    RuntimeImports = "ImmutableList"
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.IndexOf),
    JsMethod = "indexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.LastIndexOf),
    JsMethod = "lastIndexOf",
    WhenArgCount = 1
)]
[assembly: MapMethod(typeof(ImmutableList), nameof(ImmutableList.Remove), JsMethod = "remove")]
[assembly: MapMethod(
    typeof(ImmutableList),
    nameof(ImmutableList.RemoveRange),
    JsMethod = "removeItems"
)]
[assembly: MapMethod(typeof(ImmutableList), nameof(ImmutableList.Replace), JsMethod = "replace")]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.Create),
    JsTemplate = "ImmutableArray.empty<$T0>()",
    RuntimeImports = "ImmutableArray",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.Create),
    JsTemplate = "ImmutableArray.of<$T0>($0)",
    RuntimeImports = "ImmutableArray",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.Create),
    JsTemplate = "ImmutableArray.fromRange<$T0>($0.toArray(), $1, $2)",
    RuntimeImports = "ImmutableArray",
    WhenParameterTypes = new[] { typeof(ImmutableArray<>), typeof(int), typeof(int) }
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.Create),
    JsTemplate = "ImmutableArray.fromRange<$T0>($0, $1, $2)",
    RuntimeImports = "ImmutableArray",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.CreateRange),
    JsTemplate = "ImmutableArray.from<$T0>($0)",
    RuntimeImports = "ImmutableArray",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.CreateBuilder),
    JsTemplate = "new ImmutableArrayBuilder<$T0>()",
    RuntimeImports = "ImmutableArrayBuilder",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.ToImmutableArray),
    JsTemplate = "ImmutableArray.from<$T0>($this)",
    RuntimeImports = "ImmutableArray"
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.BinarySearch),
    JsTemplate = "$this.binarySearch($0)",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.BinarySearch),
    JsTemplate = "$0.binarySearch($1)",
    WhenParameterTypeNames = new[] { "System.Collections.Immutable.ImmutableArray<T>", "T" }
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.BinarySearch),
    JsTemplate = "$0.binarySearch($1, $2)",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableArray),
    nameof(ImmutableArray.BinarySearch),
    JsTemplate = "$this.binarySearch($0, $1)",
    WhenParameterTypeNames = new[] { "T", "System.Collections.Generic.IComparer<T>" }
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.Create),
    JsTemplate = "ImmutableHashSet.of<$T0>([], $0)",
    RuntimeImports = "ImmutableHashSet",
    WhenParameterTypes = new[] { typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.Create),
    JsTemplate = "ImmutableHashSet.empty<$T0>()",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.Create),
    JsTemplate = "ImmutableHashSet.of<$T0>($0)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.Create),
    JsTemplate = "ImmutableHashSet.of<$T0>($1, $0)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.CreateRange),
    JsTemplate = "ImmutableHashSet.from<$T0>($0)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.CreateRange),
    JsTemplate = "ImmutableHashSet.from<$T0>($1, $0)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.CreateBuilder),
    JsTemplate = "new ImmutableHashSetBuilder<$T0>()",
    RuntimeImports = "ImmutableHashSetBuilder",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.CreateBuilder),
    JsTemplate = "new ImmutableHashSetBuilder<$T0>(null, $0)",
    RuntimeImports = "ImmutableHashSetBuilder",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.ToImmutableHashSet),
    JsTemplate = "ImmutableHashSet.from<$T0>($this)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableHashSet),
    nameof(ImmutableHashSet.ToImmutableHashSet),
    JsTemplate = "ImmutableHashSet.from<$T0>($this, $0)",
    RuntimeImports = "ImmutableHashSet",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.Create),
    JsTemplate = "ImmutableDictionary.empty<$T0, $T1>()",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.Create),
    JsTemplate = "ImmutableDictionary.create<$T0, $T1>($0)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.Create),
    JsTemplate = "ImmutableDictionary.create<$T0, $T1>($0, $1)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateRange),
    JsTemplate = "ImmutableDictionary.from<$T0, $T1>($0)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateRange),
    JsTemplate = "ImmutableDictionary.from<$T0, $T1>($1, $0)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateRange),
    JsTemplate = "ImmutableDictionary.from<$T0, $T1>($2, $0, $1)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateBuilder),
    JsTemplate = "new ImmutableDictionaryBuilder<$T0, $T1>()",
    RuntimeImports = "ImmutableDictionaryBuilder",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateBuilder),
    JsTemplate = "new ImmutableDictionaryBuilder<$T0, $T1>(null, $0)",
    RuntimeImports = "ImmutableDictionaryBuilder",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.CreateBuilder),
    JsTemplate = "new ImmutableDictionaryBuilder<$T0, $T1>(null, $0, $1)",
    RuntimeImports = "ImmutableDictionaryBuilder",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.from($this)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.fromSelectors($this, $0, null)",
    RuntimeImports = "ImmutableDictionary",
    WhenParameterTypes = new[] { typeof(Func<,>) }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.fromSelectors($this, $0, null, $1)",
    RuntimeImports = "ImmutableDictionary",
    WhenParameterTypes = new[] { typeof(Func<,>), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.fromSelectors($this, $0, $1)",
    RuntimeImports = "ImmutableDictionary",
    WhenParameterTypes = new[] { typeof(Func<,>), typeof(Func<,>) }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.fromSelectors($this, $0, $1, $2)",
    RuntimeImports = "ImmutableDictionary",
    WhenParameterTypes = new[] { typeof(Func<,>), typeof(Func<,>), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.fromSelectors($this, $0, $1, $2, $3)",
    RuntimeImports = "ImmutableDictionary",
    WhenParameterTypes = new[]
    {
        typeof(Func<,>),
        typeof(Func<,>),
        typeof(IEqualityComparer<>),
        typeof(IEqualityComparer<>),
    }
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.from($this, $0)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.ToImmutableDictionary),
    JsTemplate = "ImmutableDictionary.from($this, $0, $1)",
    RuntimeImports = "ImmutableDictionary",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ImmutableDictionary),
    nameof(ImmutableDictionary.Contains),
    JsTemplate = "$this.contains([$0, $1])"
)]
