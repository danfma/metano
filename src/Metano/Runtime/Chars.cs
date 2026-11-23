// Declarative BCL → JavaScript mappings for the `System.Char` category tests (#686). A `char`
// is a one-code-unit string, so each test is a `metano-runtime` helper that reads the Unicode
// general category as .NET does (`IsLetter` is `\p{L}`, `IsWhiteSpace` also admits U+0009-U+000D
// and U+0085). Only the `char` overloads are mapped; `IsLetter(string, int)` and its siblings
// stay unmapped and report MS0001.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(char),
    nameof(char.IsLetter),
    JsTemplate = "charIsLetter($0)",
    RuntimeImports = "charIsLetter",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsDigit),
    JsTemplate = "charIsDigit($0)",
    RuntimeImports = "charIsDigit",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsLetterOrDigit),
    JsTemplate = "charIsLetterOrDigit($0)",
    RuntimeImports = "charIsLetterOrDigit",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsNumber),
    JsTemplate = "charIsNumber($0)",
    RuntimeImports = "charIsNumber",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsUpper),
    JsTemplate = "charIsUpper($0)",
    RuntimeImports = "charIsUpper",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsLower),
    JsTemplate = "charIsLower($0)",
    RuntimeImports = "charIsLower",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsPunctuation),
    JsTemplate = "charIsPunctuation($0)",
    RuntimeImports = "charIsPunctuation",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsSymbol),
    JsTemplate = "charIsSymbol($0)",
    RuntimeImports = "charIsSymbol",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsSeparator),
    JsTemplate = "charIsSeparator($0)",
    RuntimeImports = "charIsSeparator",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsControl),
    JsTemplate = "charIsControl($0)",
    RuntimeImports = "charIsControl",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(char),
    nameof(char.IsWhiteSpace),
    JsTemplate = "charIsWhiteSpace($0)",
    RuntimeImports = "charIsWhiteSpace",
    WhenParameterTypes = [typeof(char)]
)]
