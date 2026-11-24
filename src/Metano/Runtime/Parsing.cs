// Declarative BCL → JavaScript mappings for `TryParse(string, out T)` on the primitive
// types.
//
// The compiler lowers a call with `out` arguments to a call returning `[result, out…]`
// and keeps one argument slot per `out` parameter filled with `default(T)` (`$1` here).
// The runtime helpers report the default themselves, so the templates use `$0` only.
// For `int`, `double`, `float` and `bool`, `WhenArgCount = 2` limits the mapping to the
// two-argument overloads (`(string, out T)` and its span forms); their `NumberStyles` /
// `IFormatProvider` overloads stay unmapped.
//
// `int`, `uint`, `short`, `ushort`, `byte` and `sbyte` (#668): `Parse` and `TryParse` lower to
// the `metano-runtime` parsers (`parseInt32`, `tryParseInt32`, ...), which follow .NET's
// grammar and throw `FormatException` / `OverflowException` where `Number.parseInt` would
// return `12` for `"12abc"`. Every `string` overload is mapped, the `NumberStyles` ones
// folded to their flags value as for `long`; the `ReadOnlySpan<char>` overloads of `Parse`
// stay unmapped, and the two-argument `TryParse` fallbacks below cover the span ones.
// `double.Parse`, `float.Parse` and `bool.Parse` map the `string` overloads without styles.
//
// `long` and `ulong` (spec 012 FR-038): `Parse` and `TryParse` lower to the
// `metano-runtime` parsers, which return a `bigint`. Every `string` overload is mapped,
// the `NumberStyles` / `IFormatProvider` ones included, with the `NumberStyles` argument
// folded to its flags value by the compiler.

using System.Globalization;
using Metano.Annotations;

[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0, $1)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Parse),
    JsTemplate = "parseInt64($0, $1)",
    RuntimeImports = "parseInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64($0)",
    RuntimeImports = "tryParseInt64",
    WhenParameterTypes = [typeof(string), typeof(long)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64($0)",
    RuntimeImports = "tryParseInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(long)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.TryParse),
    JsTemplate = "tryParseInt64($0, $1)",
    RuntimeImports = "tryParseInt64",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(long),
    ]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0, $1)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Parse),
    JsTemplate = "parseUInt64($0, $1)",
    RuntimeImports = "parseUInt64",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.TryParse),
    JsTemplate = "tryParseUInt64($0)",
    RuntimeImports = "tryParseUInt64",
    WhenParameterTypes = [typeof(string), typeof(ulong)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.TryParse),
    JsTemplate = "tryParseUInt64($0)",
    RuntimeImports = "tryParseUInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(ulong)]
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
    ]
)]

[assembly: MapMethod(
    typeof(int),
    nameof(int.TryParse),
    JsTemplate = "tryParseInt32($0)",
    RuntimeImports = "tryParseInt32",
    WhenArgCount = 2
)]
// The `string` overload of `long.TryParse` matches its typed declaration above; this
// one stays for the other two-argument overloads (`ReadOnlySpan<char>`, which is a
// `string` in the output), as the `int` declaration does.
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
    JsTemplate = "tryParseFloat32($0)",
    RuntimeImports = "tryParseFloat32",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(bool),
    nameof(bool.TryParse),
    JsTemplate = "tryParseBoolean($0)",
    RuntimeImports = "tryParseBoolean",
    WhenArgCount = 2
)]

[assembly: MapMethod(
    typeof(int),
    nameof(int.Parse),
    JsTemplate = "parseInt32($0)",
    RuntimeImports = "parseInt32",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.Parse),
    JsTemplate = "parseInt32($0)",
    RuntimeImports = "parseInt32",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.Parse),
    JsTemplate = "parseInt32($0, $1)",
    RuntimeImports = "parseInt32",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.Parse),
    JsTemplate = "parseInt32($0, $1)",
    RuntimeImports = "parseInt32",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.TryParse),
    JsTemplate = "tryParseInt32($0)",
    RuntimeImports = "tryParseInt32",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(int)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.TryParse),
    JsTemplate = "tryParseInt32($0, $1)",
    RuntimeImports = "tryParseInt32",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(int),
    ]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.Parse),
    JsTemplate = "parseUInt32($0)",
    RuntimeImports = "parseUInt32",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.Parse),
    JsTemplate = "parseUInt32($0)",
    RuntimeImports = "parseUInt32",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.Parse),
    JsTemplate = "parseUInt32($0, $1)",
    RuntimeImports = "parseUInt32",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.Parse),
    JsTemplate = "parseUInt32($0, $1)",
    RuntimeImports = "parseUInt32",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.TryParse),
    JsTemplate = "tryParseUInt32($0)",
    RuntimeImports = "tryParseUInt32",
    WhenParameterTypes = [typeof(string), typeof(uint)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.TryParse),
    JsTemplate = "tryParseUInt32($0)",
    RuntimeImports = "tryParseUInt32",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(uint)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.TryParse),
    JsTemplate = "tryParseUInt32($0, $1)",
    RuntimeImports = "tryParseUInt32",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(uint),
    ]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.Parse),
    JsTemplate = "parseInt16($0)",
    RuntimeImports = "parseInt16",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.Parse),
    JsTemplate = "parseInt16($0)",
    RuntimeImports = "parseInt16",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.Parse),
    JsTemplate = "parseInt16($0, $1)",
    RuntimeImports = "parseInt16",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.Parse),
    JsTemplate = "parseInt16($0, $1)",
    RuntimeImports = "parseInt16",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.TryParse),
    JsTemplate = "tryParseInt16($0)",
    RuntimeImports = "tryParseInt16",
    WhenParameterTypes = [typeof(string), typeof(short)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.TryParse),
    JsTemplate = "tryParseInt16($0)",
    RuntimeImports = "tryParseInt16",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(short)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.TryParse),
    JsTemplate = "tryParseInt16($0, $1)",
    RuntimeImports = "tryParseInt16",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(short),
    ]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.Parse),
    JsTemplate = "parseUInt16($0)",
    RuntimeImports = "parseUInt16",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.Parse),
    JsTemplate = "parseUInt16($0)",
    RuntimeImports = "parseUInt16",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.Parse),
    JsTemplate = "parseUInt16($0, $1)",
    RuntimeImports = "parseUInt16",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.Parse),
    JsTemplate = "parseUInt16($0, $1)",
    RuntimeImports = "parseUInt16",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.TryParse),
    JsTemplate = "tryParseUInt16($0)",
    RuntimeImports = "tryParseUInt16",
    WhenParameterTypes = [typeof(string), typeof(ushort)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.TryParse),
    JsTemplate = "tryParseUInt16($0)",
    RuntimeImports = "tryParseUInt16",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(ushort)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.TryParse),
    JsTemplate = "tryParseUInt16($0, $1)",
    RuntimeImports = "tryParseUInt16",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(ushort),
    ]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.Parse),
    JsTemplate = "parseByte($0)",
    RuntimeImports = "parseByte",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.Parse),
    JsTemplate = "parseByte($0)",
    RuntimeImports = "parseByte",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.Parse),
    JsTemplate = "parseByte($0, $1)",
    RuntimeImports = "parseByte",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.Parse),
    JsTemplate = "parseByte($0, $1)",
    RuntimeImports = "parseByte",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.TryParse),
    JsTemplate = "tryParseByte($0)",
    RuntimeImports = "tryParseByte",
    WhenParameterTypes = [typeof(string), typeof(byte)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.TryParse),
    JsTemplate = "tryParseByte($0)",
    RuntimeImports = "tryParseByte",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(byte)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.TryParse),
    JsTemplate = "tryParseByte($0, $1)",
    RuntimeImports = "tryParseByte",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(byte),
    ]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.Parse),
    JsTemplate = "parseSByte($0)",
    RuntimeImports = "parseSByte",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.Parse),
    JsTemplate = "parseSByte($0)",
    RuntimeImports = "parseSByte",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.Parse),
    JsTemplate = "parseSByte($0, $1)",
    RuntimeImports = "parseSByte",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.Parse),
    JsTemplate = "parseSByte($0, $1)",
    RuntimeImports = "parseSByte",
    WhenParameterTypes = [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.TryParse),
    JsTemplate = "tryParseSByte($0)",
    RuntimeImports = "tryParseSByte",
    WhenParameterTypes = [typeof(string), typeof(sbyte)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.TryParse),
    JsTemplate = "tryParseSByte($0)",
    RuntimeImports = "tryParseSByte",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(sbyte)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.TryParse),
    JsTemplate = "tryParseSByte($0, $1)",
    RuntimeImports = "tryParseSByte",
    WhenParameterTypes = [
        typeof(string),
        typeof(NumberStyles),
        typeof(IFormatProvider),
        typeof(sbyte),
    ]
)]

// `double.Parse(string)` / `float.Parse(string)` / `bool.Parse(string)` (and the
// `IFormatProvider` forms of the numbers, which the invariant-culture runtime ignores).
[assembly: MapMethod(
    typeof(double),
    nameof(double.Parse),
    JsTemplate = "parseFloat64($0)",
    RuntimeImports = "parseFloat64",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.Parse),
    JsTemplate = "parseFloat64($0)",
    RuntimeImports = "parseFloat64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.Parse),
    JsTemplate = "parseFloat32($0)",
    RuntimeImports = "parseFloat32",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.Parse),
    JsTemplate = "parseFloat32($0)",
    RuntimeImports = "parseFloat32",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(bool),
    nameof(bool.Parse),
    JsTemplate = "parseBoolean($0)",
    RuntimeImports = "parseBoolean",
    WhenParameterTypes = [typeof(string)]
)]
