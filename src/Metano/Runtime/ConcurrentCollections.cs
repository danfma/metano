// Declarative BCL → JavaScript mappings for `ConcurrentDictionary<K, V>` (spec 011
// FR-054, stage 011h): the `metano-runtime` `ConcurrentDictionary` class over the shared
// hash core, whose enumerator never throws on modification
// (contracts/runtime-collections.md § ConcurrentDictionary). Members are plain renames;
// the indexer is the element-access bridge's `get` / `set`. The value and factory forms of
// `GetOrAdd` / `AddOrUpdate` are distinct runtime members chosen by the C# overload (its
// parameter types), never by `typeof` at run time, since `V` may itself be a function.
// `TryGetValue` / `TryRemove` take the `out` lowering's `default(V)`. Members the runtime
// does not implement (`TryRemove(KeyValuePair)`, `Comparer`, the alternate lookups) have
// no mapping and report MS0001.

using System.Collections.Concurrent;
using Metano.Annotations;

[assembly: MapProperty(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.IsEmpty),
    JsProperty = "isEmpty"
)]
[assembly: MapProperty(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.Keys),
    JsProperty = "keys"
)]
[assembly: MapProperty(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.Values),
    JsProperty = "values"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.Clear),
    JsMethod = "clear"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.ContainsKey),
    JsMethod = "containsKey"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.ToArray),
    JsMethod = "toArray"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.TryAdd),
    JsMethod = "tryAdd"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.TryGetValue),
    JsMethod = "tryGetValue"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.TryUpdate),
    JsMethod = "tryUpdate"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.TryRemove),
    JsMethod = "tryRemove",
    WhenParameterTypeNames = new[] { "TKey", "TValue" }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.GetOrAdd),
    JsMethod = "getOrAdd",
    WhenParameterTypeNames = new[] { "TKey", "TValue" }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.GetOrAdd),
    JsMethod = "getOrAddWith"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.AddOrUpdate),
    JsMethod = "addOrUpdate",
    WhenParameterTypeNames = new[] { "TKey", "TValue", "System.Func<T1, T2, TResult>" }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    nameof(ConcurrentDictionary<int, int>.AddOrUpdate),
    JsMethod = "addOrUpdateWith"
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    "GetValueOrDefault",
    JsMethod = "getValueOrDefault",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new ConcurrentDictionary<$T0, $T1>()",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "ConcurrentDictionary.withCapacity<$T0, $T1>($0, $1)",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new[] { typeof(int), typeof(int) }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "ConcurrentDictionary.withCapacity<$T0, $T1>($0, $1, $2)",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new[] { typeof(int), typeof(int), typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new ConcurrentDictionary<$T0, $T1>(null, $0)",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new[] { typeof(IEqualityComparer<>) }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "ConcurrentDictionary.from($0)",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
[assembly: MapMethod(
    typeof(ConcurrentDictionary<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "ConcurrentDictionary.from($0, $1)",
    RuntimeImports = "ConcurrentDictionary",
    WhenParameterTypes = new[] { typeof(IEnumerable<>), typeof(IEqualityComparer<>) }
)]
