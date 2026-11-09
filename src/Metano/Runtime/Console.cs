// Declarative BCL → target mappings for System.Console.
//
// JS: WriteLine → console.log($0) via template (the JS console object is a
// lowercase identifier, not a rename of `Console`).
// Dart: WriteLine → top-level `print(...)` function. The `WhenArgCount = 1`
// filter restricts the rename to the single-arg overloads — the format-string
// overloads `Console.WriteLine(format, args...)` would lower to `print(format,
// args...)`, which is invalid because Dart's `print` only accepts a single
// object argument. Those overloads stay un-mapped on the Dart side until a
// proper formatting strategy lands.

//
// A `long` / `ulong` argument (spec 012 FR-072) is a `bigint`: `console.log(5n)`
// prints `5n`, so the value goes through `String` first and prints `5`, as C# does.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(Console),
    nameof(Console.WriteLine),
    JsTemplate = "console.log(String($0))",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(Console),
    nameof(Console.WriteLine),
    JsTemplate = "console.log(String($0))",
    WhenParameterTypes = [typeof(ulong)]
)]

[assembly: MapMethod(
    typeof(Console),
    nameof(Console.WriteLine),
    JsTemplate = "console.log($0)",
    DartMethod = "print",
    WhenArgCount = 1
)]
