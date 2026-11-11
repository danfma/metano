// Declarative BCL → JavaScript mappings for ToString(format) on the numbers held in a JS
// `number` (baseline FR-051, #323): the integers of 32 bits or fewer through `formatInteger`,
// which takes the bit width a negative hex value is printed on, and `double` through
// `formatDouble`. Both implement .NET's invariant-culture standard numeric formats, as
// `formatInt64` does for `long` / `ulong` (Int64.cs) and as an interpolation hole's format
// clause does. The provider is ignored (#308) and, as in every provider overload, not
// evaluated. The core reports a constant format the runtime does not implement before these
// mappings run (MS0001); a format known only at run time reaches the formatter, which throws
// FormatException or NotSupportedException for what it does not implement, and a null one
// formats as `G`. `float` has no mapping, so its ToString(format) always reports.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.ToString),
    JsTemplate = "formatInteger($this, 8, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.ToString),
    JsTemplate = "formatInteger($this, 8, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.ToString),
    JsTemplate = "formatInteger($this, 8, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.ToString),
    JsTemplate = "formatInteger($this, 8, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.ToString),
    JsTemplate = "formatInteger($this, 16, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.ToString),
    JsTemplate = "formatInteger($this, 16, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.ToString),
    JsTemplate = "formatInteger($this, 16, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.ToString),
    JsTemplate = "formatInteger($this, 16, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.ToString),
    JsTemplate = "formatInteger($this, 32, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(int),
    nameof(int.ToString),
    JsTemplate = "formatInteger($this, 32, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.ToString),
    JsTemplate = "formatInteger($this, 32, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.ToString),
    JsTemplate = "formatInteger($this, 32, $0)",
    RuntimeImports = "formatInteger",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.ToString),
    JsTemplate = "formatDouble($this, $0)",
    RuntimeImports = "formatDouble",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.ToString),
    JsTemplate = "formatDouble($this, $0)",
    RuntimeImports = "formatDouble",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
