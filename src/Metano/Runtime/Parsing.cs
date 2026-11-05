// Declarative BCL → JavaScript mappings for `TryParse(string, out T)` on the primitive
// types.
//
// The compiler lowers a call with `out` arguments to a call returning `[result, out…]`
// and keeps one argument slot per `out` parameter filled with `default(T)` (`$1` here).
// The runtime helpers report the default themselves, so the templates use `$0` only.
// `WhenArgCount = 2` limits the mapping to the `(string, out T)` overload; the
// `NumberStyles` / `IFormatProvider` overloads stay unmapped.

//
// `long` and `ulong` (spec 012 FR-038): while 64-bit integers are `bigint` values
// (`WhenInt64IsBigInt`, a staging filter), `Parse` and `TryParse` lower to the
// `metano-runtime` parsers with the `NumberStyles` argument folded to its flags value
// by the compiler. They precede the 2.x `long.TryParse` declaration below, which
// returns a `number`. The signed `TryParse` helper is `tryParseInt64Value` until the
// 2.x `tryParseInt64` goes with the staging switch.

using System.Globalization;
using Metano.Annotations;

[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0, $1)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0, $1)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64Value($0)",
    RuntimeImports = "tryParseInt64Value",
    WhenParameterTypes = [typeof(string), typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64Value($0)",
    RuntimeImports = "tryParseInt64Value",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64Value($0, $1)",
    RuntimeImports = "tryParseInt64Value",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(long),
    ],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0, $1)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0, $1)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.TryParse),
    JsTemplate = "tryParseUInt64($0)",
    RuntimeImports = "tryParseUInt64",
    WhenParameterTypes = [typeof(string), typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.TryParse),
    JsTemplate = "tryParseUInt64($0)",
    RuntimeImports = "tryParseUInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.TryParse),
    JsTemplate = "tryParseUInt64($0, $1)",
    RuntimeImports = "tryParseUInt64",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(ulong),
    ],
    WhenInt64IsBigInt = true
)]

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
