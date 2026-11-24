// Declarative BCL → JavaScript mapping for the static `KeyValuePair.Create(key, value)` (#584):
// the same `[key, value]` tuple the `KeyValuePair<,>` constructor builds (Dictionaries.cs).

using Metano.Annotations;

[assembly: MapMethod(
    typeof(KeyValuePair),
    nameof(KeyValuePair.Create),
    JsTemplate = "[$0, $1] as [$T0, $T1]"
)]
