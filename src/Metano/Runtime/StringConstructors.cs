// Declarative BCL → JavaScript mappings for the constructors of `System.String` (#695). A
// `char` is a one-code-unit string and a `char[]` a JS array of them, so `new string(chars)`
// joins the array and `new string(c, count)` repeats the char. Without a mapping the call
// printed `new Object(chars)`. The span and pointer overloads, and the range overload
// `new string(chars, start, length)`, stay unmapped and report MS0001.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(string),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "$0.join(\"\")",
    WhenParameterTypes = [typeof(char[])]
)]
[assembly: MapMethod(
    typeof(string),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "$0.repeat($1)",
    WhenParameterTypes = [typeof(char), typeof(int)]
)]
