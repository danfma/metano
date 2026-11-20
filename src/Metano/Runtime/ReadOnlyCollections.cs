// Declarative BCL → JavaScript mappings for the read-only list view (spec 011 FR-054,
// stage 011h): `ReadOnlyCollection<T>` is the `metano-runtime` `ReadOnlyList` class, a
// live view over a `List` or an array (contracts/runtime-collections.md § ReadOnlyList).
// Members are plain renames; the indexer is the element-access bridge's `get`. The view
// is built by its constructor, by `List<T>.AsReadOnly()` (the free `asReadOnly` helper,
// which throws NullReferenceException for a null list as the member call does) and by
// `Array.AsReadOnly(array)`.

using System.Collections.ObjectModel;
using Metano.Annotations;

[assembly: MapProperty(
    typeof(ReadOnlyCollection<>),
    nameof(ReadOnlyCollection<int>.Count),
    JsProperty = "count"
)]
[assembly: MapProperty(
    typeof(ReadOnlyCollection<>),
    nameof(ReadOnlyCollection<int>.Empty),
    JsTemplate = "ReadOnlyList.empty()",
    RuntimeImports = "ReadOnlyList"
)]
[assembly: MapMethod(
    typeof(ReadOnlyCollection<>),
    nameof(ReadOnlyCollection<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(
    typeof(ReadOnlyCollection<>),
    nameof(ReadOnlyCollection<int>.IndexOf),
    JsMethod = "indexOf"
)]
[assembly: MapMethod(
    typeof(ReadOnlyCollection<>),
    nameof(ReadOnlyCollection<int>.CopyTo),
    JsMethod = "copyTo"
)]
[assembly: MapMethod(
    typeof(ReadOnlyCollection<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new ReadOnlyList<$T0>($0)",
    RuntimeImports = "ReadOnlyList",
    WhenParameterTypes = new[] { typeof(IList<>) }
)]
[assembly: MapMethod(
    typeof(List<>),
    nameof(List<int>.AsReadOnly),
    JsTemplate = "asReadOnly($this)",
    RuntimeImports = "asReadOnly"
)]
[assembly: MapMethod(
    typeof(Array),
    nameof(Array.AsReadOnly),
    JsTemplate = "ReadOnlyList.fromArray($0)",
    RuntimeImports = "ReadOnlyList"
)]
