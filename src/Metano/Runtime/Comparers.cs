// Declarative BCL → JavaScript mappings for System.StringComparer (#687): the ordinal
// comparers are the `StringComparer` runtime singletons, which a Dictionary<string, V>, a
// HashSet<string> or a sort takes as its comparer. The culture-sensitive comparers
// (CurrentCulture, InvariantCulture, ...) have no counterpart and stay unmapped.

using Metano.Annotations;

[assembly: MapProperty(
    typeof(StringComparer),
    nameof(StringComparer.Ordinal),
    JsTemplate = "StringComparer.ordinal",
    RuntimeImports = "StringComparer"
)]
[assembly: MapProperty(
    typeof(StringComparer),
    nameof(StringComparer.OrdinalIgnoreCase),
    JsTemplate = "StringComparer.ordinalIgnoreCase",
    RuntimeImports = "StringComparer"
)]

// IComparable<T>.CompareTo / IComparable.CompareTo called through the interface or a type
// parameter (#769): the receiver may be a string or a number, which have no `compareTo` in
// JavaScript, so the call goes through `comparableCompareTo`. A string compares as
// string.CompareTo (culture-sensitive); any other value in Comparer<T>.Default's order,
// which calls an object's own `compareTo`. A call on a concrete type keeps that type's
// mapping (or its own `compareTo`).

[assembly: MapMethod(
    typeof(IComparable<>),
    nameof(IComparable<int>.CompareTo),
    JsTemplate = "comparableCompareTo($this, $0)",
    RuntimeImports = "comparableCompareTo"
)]
[assembly: MapMethod(
    typeof(IComparable),
    nameof(IComparable.CompareTo),
    JsTemplate = "comparableCompareTo($this, $0)",
    RuntimeImports = "comparableCompareTo"
)]
