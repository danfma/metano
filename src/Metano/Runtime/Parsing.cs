// Declarative BCL → JavaScript mappings for `TryParse(string, out T)` on the primitive
// types.
//
// The compiler lowers a call with `out` arguments to a call returning `[result, out…]`
// and keeps one argument slot per `out` parameter filled with `default(T)` (`$1` here).
// The runtime helpers report the default themselves, so the templates use `$0` only.
// `WhenArgCount = 2` limits the mapping to the `(string, out T)` overload; the
// `NumberStyles` / `IFormatProvider` overloads stay unmapped.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(int),
    nameof(int.TryParse),
    JsTemplate = "tryParseInt32($0)",
    RuntimeImports = "tryParseInt32",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64($0)",
    RuntimeImports = "tryParseInt64",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.TryParse),
    JsTemplate = "tryParseFloat64($0)",
    RuntimeImports = "tryParseFloat64",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.TryParse),
    JsTemplate = "tryParseFloat64($0)",
    RuntimeImports = "tryParseFloat64",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(bool),
    nameof(bool.TryParse),
    JsTemplate = "tryParseBoolean($0)",
    RuntimeImports = "tryParseBoolean",
    WhenArgCount = 2
)]
