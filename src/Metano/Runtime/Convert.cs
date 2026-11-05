// Declarative BCL → JavaScript mappings for `System.Convert` into and out of `long`
// and `ulong` (spec 012 FR-039), applied only while 64-bit integers are `bigint` values
// (`WhenInt64IsBigInt`, a staging filter); `Convert` stays unmapped in the 2.x output.
//
// Unlike a cast, `Convert` rounds a fraction half to even, always checks the range
// and reports .NET's per-type message (`Value was either too large or too small for an
// Int32.`). Each source type has its own overload, so each declaration names it:
// `double` / `float`, `decimal`, `string` (with an optional base), `object`, `bool`,
// `char`, the narrower integers (a negative one checks against `ulong`) and the two
// 64-bit types themselves. `ToDouble` / `ToSingle` convert as the implicit conversions
// do; `ToDecimal(long)` stays unmapped (a template cannot import decimal.js).

using Metano.Annotations;

[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertDoubleToInt64($0)",
    RuntimeImports = "convertDoubleToInt64",
    WhenParameterTypes = [typeof(double)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertDoubleToInt64($0)",
    RuntimeImports = "convertDoubleToInt64",
    WhenParameterTypes = [typeof(float)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertDecimalToInt64($0)",
    RuntimeImports = "convertDecimalToInt64",
    WhenParameterTypes = [typeof(decimal)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertStringToInt64($0)",
    RuntimeImports = "convertStringToInt64",
    WhenParameterTypes = [typeof(string)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertStringToInt64($0)",
    RuntimeImports = "convertStringToInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertStringToInt64($0, $1)",
    RuntimeImports = "convertStringToInt64",
    WhenParameterTypes = [typeof(string), typeof(int)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertObjectToInt64($0)",
    RuntimeImports = "convertObjectToInt64",
    WhenParameterTypes = [typeof(object)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertObjectToInt64($0)",
    RuntimeImports = "convertObjectToInt64",
    WhenParameterTypes = [typeof(object), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "($0 ? 1n : 0n)",
    WhenParameterTypes = [typeof(bool)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0.charCodeAt(0))",
    WhenParameterTypes = [typeof(char)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(byte)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(ushort)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(uint)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(sbyte)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(short)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(int)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "$0",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt64),
    JsTemplate = "convertNarrow($0, \"Int64\")",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertDoubleToUInt64($0)",
    RuntimeImports = "convertDoubleToUInt64",
    WhenParameterTypes = [typeof(double)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertDoubleToUInt64($0)",
    RuntimeImports = "convertDoubleToUInt64",
    WhenParameterTypes = [typeof(float)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertDecimalToUInt64($0)",
    RuntimeImports = "convertDecimalToUInt64",
    WhenParameterTypes = [typeof(decimal)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertStringToUInt64($0)",
    RuntimeImports = "convertStringToUInt64",
    WhenParameterTypes = [typeof(string)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertStringToUInt64($0)",
    RuntimeImports = "convertStringToUInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertStringToUInt64($0, $1)",
    RuntimeImports = "convertStringToUInt64",
    WhenParameterTypes = [typeof(string), typeof(int)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertObjectToUInt64($0)",
    RuntimeImports = "convertObjectToUInt64",
    WhenParameterTypes = [typeof(object)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertObjectToUInt64($0)",
    RuntimeImports = "convertObjectToUInt64",
    WhenParameterTypes = [typeof(object), typeof(IFormatProvider)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "($0 ? 1n : 0n)",
    WhenParameterTypes = [typeof(bool)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "BigInt($0.charCodeAt(0))",
    WhenParameterTypes = [typeof(char)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(byte)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(ushort)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(uint)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertNarrow(BigInt($0), \"UInt64\")",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(sbyte)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertNarrow(BigInt($0), \"UInt64\")",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(short)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertNarrow(BigInt($0), \"UInt64\")",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(int)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "convertNarrow($0, \"UInt64\")",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt64),
    JsTemplate = "$0",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToSByte),
    JsTemplate = "Number(convertNarrow($0, \"SByte\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToSByte),
    JsTemplate = "Number(convertNarrow($0, \"SByte\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToByte),
    JsTemplate = "Number(convertNarrow($0, \"Byte\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToByte),
    JsTemplate = "Number(convertNarrow($0, \"Byte\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt16),
    JsTemplate = "Number(convertNarrow($0, \"Int16\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt16),
    JsTemplate = "Number(convertNarrow($0, \"Int16\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt16),
    JsTemplate = "Number(convertNarrow($0, \"UInt16\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt16),
    JsTemplate = "Number(convertNarrow($0, \"UInt16\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt32),
    JsTemplate = "Number(convertNarrow($0, \"Int32\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToInt32),
    JsTemplate = "Number(convertNarrow($0, \"Int32\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt32),
    JsTemplate = "Number(convertNarrow($0, \"UInt32\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToUInt32),
    JsTemplate = "Number(convertNarrow($0, \"UInt32\"))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToChar),
    JsTemplate = "String.fromCharCode(Number(convertNarrow($0, \"Char\")))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToDouble),
    JsTemplate = "Number($0)",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToSingle),
    JsTemplate = "int64ToSingle($0)",
    RuntimeImports = "int64ToSingle",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToBoolean),
    JsTemplate = "($0 !== 0n)",
    WhenParameterTypes = [typeof(long)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToChar),
    JsTemplate = "String.fromCharCode(Number(convertNarrow($0, \"Char\")))",
    RuntimeImports = "convertNarrow",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToDouble),
    JsTemplate = "Number($0)",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToSingle),
    JsTemplate = "int64ToSingle($0)",
    RuntimeImports = "int64ToSingle",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
[assembly: MapMethod(
    typeof(Convert),
    nameof(Convert.ToBoolean),
    JsTemplate = "($0 !== 0n)",
    WhenParameterTypes = [typeof(ulong)],
    WhenInt64IsBigInt = true
)]
