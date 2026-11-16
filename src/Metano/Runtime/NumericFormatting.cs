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
// The bit widths below are the ones `FormatClauseLowering.BitsOf`
// (src/Metano.Compiler.TypeScript/Bridge) passes for an interpolation hole; keep the two in step.

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

// ─── Default text: ToString() (baseline FR-014, #308) ───────────
// The text a `bool`, `double` or `float` prints without a format, in the invariant culture:
// `True` / `False`, `1E-07`, `1E+21`, `-0` (JavaScript writes `true`, `1e-7`, `1e+21`, `0`).
// `$"{value}"`, `"x" + value`, `string.Join` over these types and an explicit `ToString()`
// all call the same runtime function, so a project that prints none of them imports none; the
// extractor routes the first three through the mapping below (`DefaultTextConversion`).
// `formatDouble` with no format is `G`, the shortest round-trip text. `formatSingle` reads the
// value as the `float` C# holds (the TypeScript `number` need not be single precision) and
// prints the shortest digits that read back as it. A `null` (`bool?`, `double?`, `float?`)
// prints as the empty string, as in C#. The provider is ignored, as in every provider overload.
[assembly: MapMethod(
    typeof(bool),
    nameof(bool.ToString),
    JsTemplate = "formatBoolean($this)",
    RuntimeImports = "formatBoolean",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(bool),
    nameof(bool.ToString),
    JsTemplate = "formatBoolean($this)",
    RuntimeImports = "formatBoolean",
    WhenParameterTypes = [typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.ToString),
    JsTemplate = "formatDouble($this)",
    RuntimeImports = "formatDouble",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.ToString),
    JsTemplate = "formatDouble($this)",
    RuntimeImports = "formatDouble",
    WhenParameterTypes = [typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.ToString),
    JsTemplate = "formatSingle($this)",
    RuntimeImports = "formatSingle",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.ToString),
    JsTemplate = "formatSingle($this)",
    RuntimeImports = "formatSingle",
    WhenParameterTypes = [typeof(IFormatProvider)]
)]
