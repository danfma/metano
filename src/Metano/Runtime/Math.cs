// Declarative BCL → JavaScript mappings for System.Math.
//
// JS exposes a global Math object whose method names happen to be the lowercase
// equivalents of System.Math's PascalCase methods, so the simple-rename form is enough
// for most of the helpers covered here. The receiver is preserved verbatim — the
// IdentifierHandler renders the type reference `Math` as the bare identifier `Math`,
// and the JS global has the same name.
//
// `Math.Round` is the exception: JS `Math.round` rounds a midpoint toward +Infinity and
// takes no digit count, while C# rounds a midpoint to the nearest even number by
// default and accepts a digit count and a `MidpointRounding` mode. Every `double`
// overload lowers to the runtime `roundNumber(value, digits?, mode?)` helper. The
// extractor normalizes `Math.Round(x, mode)` to `Math.Round(x, 0, mode)` first, so the
// argument count alone picks the template. The `decimal` overloads never reach these
// entries: the extractor rewrites them to `decimal.Round` (see Decimal.cs).

using Metano.Annotations;

[assembly: MapMethod(
    typeof(Math),
    nameof(Math.Round),
    JsTemplate = "roundNumber($0)",
    RuntimeImports = "roundNumber",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(Math),
    nameof(Math.Round),
    JsTemplate = "roundNumber($0, $1)",
    RuntimeImports = "roundNumber",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(Math),
    nameof(Math.Round),
    JsTemplate = "roundNumber($0, $1, $2)",
    RuntimeImports = "roundNumber",
    WhenArgCount = 3
)]

// `MidpointRounding` lowers to the runtime mirror of the enum, whose members keep the
// .NET names and numeric values.
[assembly: ExportFromBcl(
    typeof(MidpointRounding),
    ExportedName = "MidpointRounding",
    FromPackage = "metano-runtime"
)]

[assembly: MapMethod(typeof(Math), nameof(Math.Floor), JsMethod = "floor")]
[assembly: MapMethod(typeof(Math), nameof(Math.Ceiling), JsMethod = "ceil")]
[assembly: MapMethod(typeof(Math), nameof(Math.Truncate), JsMethod = "trunc")]
[assembly: MapMethod(typeof(Math), nameof(Math.Abs), JsMethod = "abs")]
[assembly: MapMethod(typeof(Math), nameof(Math.Min), JsMethod = "min")]
[assembly: MapMethod(typeof(Math), nameof(Math.Max), JsMethod = "max")]
[assembly: MapMethod(typeof(Math), nameof(Math.Sqrt), JsMethod = "sqrt")]
[assembly: MapMethod(typeof(Math), nameof(Math.Pow), JsMethod = "pow")]
