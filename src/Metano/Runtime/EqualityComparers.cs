// Declarative BCL → JavaScript mapping for `EqualityComparer<T>.Default` (#686, #403): the
// runtime's default comparer, which compares by value as .NET's does (records, structs, boxes)
// and hashes consistently with it. `Default.Equals(a, b)` and `Default.GetHashCode(x)` are
// plain member renames on the comparer object.

using Metano.Annotations;

[assembly: MapProperty(
    typeof(EqualityComparer<>),
    nameof(EqualityComparer<int>.Default),
    JsTemplate = "defaultEqualityComparerFor()",
    RuntimeImports = "defaultEqualityComparerFor"
)]
