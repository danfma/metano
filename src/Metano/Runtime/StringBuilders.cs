// Declarative BCL → JavaScript mappings for System.Text.StringBuilder (#686): the
// `metano-runtime` `StringBuilder` class, a mutable text whose mutators return the builder.
// `Append` converts each argument as .NET's overload does: text and chars as they are, the
// integers and `long` through `String`, `bool`, `double` and `float` through their formatters,
// `object` through the default text. `AppendLine` adds `\n`. The `char[]`, span,
// `StringBuilder` and `AppendFormat` overloads, the indexer and `Capacity` stay unmapped.

using System.Text;
using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(StringBuilder),
    ExportedName = "StringBuilder",
    FromPackage = "metano-runtime"
)]
[assembly: MapProperty(typeof(StringBuilder), nameof(StringBuilder.Length), JsProperty = "length")]

[assembly: MapMethod(
    typeof(StringBuilder),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new StringBuilder()",
    RuntimeImports = "StringBuilder",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new StringBuilder($0)",
    RuntimeImports = "StringBuilder",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new StringBuilder()",
    RuntimeImports = "StringBuilder",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new StringBuilder($0)",
    RuntimeImports = "StringBuilder",
    WhenParameterTypes = [typeof(string), typeof(int)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append($0)",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append($0)",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.appendRepeated($0, $1)",
    WhenParameterTypes = [typeof(char), typeof(int)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(uint)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(short)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(ushort)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(byte)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(sbyte)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(String($0))",
    WhenParameterTypes = [typeof(ulong)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(formatBoolean($0))",
    RuntimeImports = "formatBoolean",
    WhenParameterTypes = [typeof(bool)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(formatDouble($0))",
    RuntimeImports = "formatDouble",
    WhenParameterTypes = [typeof(double)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(formatSingle($0))",
    RuntimeImports = "formatSingle",
    WhenParameterTypes = [typeof(float)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Append),
    JsTemplate = "$this.append(formatText($0))",
    RuntimeImports = "formatText",
    WhenParameterTypes = [typeof(object)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.AppendLine),
    JsMethod = "appendLine"
)]
[assembly: MapMethod(typeof(StringBuilder), nameof(StringBuilder.Clear), JsMethod = "clear")]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Insert),
    JsTemplate = "$this.insert($0, $1)",
    WhenParameterTypes = [typeof(int), typeof(string)]
)]
[assembly: MapMethod(typeof(StringBuilder), nameof(StringBuilder.Remove), JsMethod = "remove")]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Replace),
    JsTemplate = "$this.replace($0, $1)",
    WhenParameterTypes = [typeof(string), typeof(string)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.Replace),
    JsTemplate = "$this.replace($0, $1)",
    WhenParameterTypes = [typeof(char), typeof(char)]
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.ToString),
    JsMethod = "toString",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(StringBuilder),
    nameof(StringBuilder.ToString),
    JsMethod = "toSubstring",
    WhenArgCount = 2
)]
