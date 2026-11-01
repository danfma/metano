// Declarative BCL → JavaScript mappings for System.Object's equality and hashing.
//
// GetHashCode: the extractor gives every `x.GetHashCode()` call the origin of
// `object.GetHashCode`, whichever override it binds to, so this one mapping covers
// primitives, strings, enums, records and user classes. The runtime `valueHashCode`
// delegates to the value's `hashCode()` (records synthesize it; a user override of
// `GetHashCode` is emitted under that name) and hashes primitives itself; `null`
// hashes to 0, as `Nullable<T>.GetHashCode` does.
//
// Equals(a, b) / ReferenceEquals(a, b): the static forms. `object.Equals(a, b)` is
// true when both are null or `a.Equals(b)`, the contract of the runtime
// `objectEquals`: it delegates to an `equals` method and compares plain object
// literals by shape, but compares any other object (a class without an Equals
// override, an array, a list) by reference, as C# does.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(object),
    nameof(object.GetHashCode),
    JsTemplate = "valueHashCode($this)",
    RuntimeImports = "valueHashCode"
)]
[assembly: MapMethod(
    typeof(object),
    nameof(object.Equals),
    JsTemplate = "objectEquals($0, $1)",
    RuntimeImports = "objectEquals",
    WhenArgCount = 2
)]
[assembly: MapMethod(typeof(object), nameof(object.ReferenceEquals), JsTemplate = "($0 === $1)")]
