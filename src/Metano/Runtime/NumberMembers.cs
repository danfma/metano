// Declarative BCL → JavaScript mappings for `CompareTo` on the numeric primitives (#686), which a
// JavaScript number does not have. `int`, `uint`, `double` and `float` return -1, 0 or 1 (a
// double puts NaN first); `short`, `ushort`, `byte`, `sbyte` and `char` return the difference
// of the two values, as .NET's implementations do.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(int),
    nameof(int.CompareTo),
    JsTemplate = "compareNumber($this, $0)",
    RuntimeImports = "compareNumber",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(uint),
    nameof(uint.CompareTo),
    JsTemplate = "compareNumber($this, $0)",
    RuntimeImports = "compareNumber",
    WhenParameterTypes = [typeof(uint)]
)]
[assembly: MapMethod(
    typeof(double),
    nameof(double.CompareTo),
    JsTemplate = "compareNumber($this, $0)",
    RuntimeImports = "compareNumber",
    WhenParameterTypes = [typeof(double)]
)]
[assembly: MapMethod(
    typeof(float),
    nameof(float.CompareTo),
    JsTemplate = "compareNumber($this, $0)",
    RuntimeImports = "compareNumber",
    WhenParameterTypes = [typeof(float)]
)]
[assembly: MapMethod(
    typeof(short),
    nameof(short.CompareTo),
    JsTemplate = "($this - $0)",
    WhenParameterTypes = [typeof(short)]
)]
[assembly: MapMethod(
    typeof(ushort),
    nameof(ushort.CompareTo),
    JsTemplate = "($this - $0)",
    WhenParameterTypes = [typeof(ushort)]
)]
[assembly: MapMethod(
    typeof(byte),
    nameof(byte.CompareTo),
    JsTemplate = "($this - $0)",
    WhenParameterTypes = [typeof(byte)]
)]
[assembly: MapMethod(
    typeof(sbyte),
    nameof(sbyte.CompareTo),
    JsTemplate = "($this - $0)",
    WhenParameterTypes = [typeof(sbyte)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.CompareTo),
    JsTemplate = "($this.charCodeAt(0) - $0.charCodeAt(0))",
    WhenParameterTypes = [typeof(char)]
)]
