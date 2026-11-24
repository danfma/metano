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
