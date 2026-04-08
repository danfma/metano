// Declarative BCL → JavaScript mappings for System.Math.
//
// JS exposes a global Math object whose method names happen to be the lowercase
// equivalents of System.Math's PascalCase methods, so the simple-rename form is enough
// for everything covered here. The receiver is preserved verbatim — `Math.Round(x)` in
// C# becomes `Math.round(x)` in JS because the IdentifierHandler renders the type
// reference `Math` as the bare identifier `Math`, and the JS global has the same name.

using System;
using MetaSharp.Annotations;

[assembly: MapMethod(typeof(Math), nameof(Math.Round), JsMethod = "round")]
[assembly: MapMethod(typeof(Math), nameof(Math.Floor), JsMethod = "floor")]
[assembly: MapMethod(typeof(Math), nameof(Math.Ceiling), JsMethod = "ceil")]
[assembly: MapMethod(typeof(Math), nameof(Math.Abs), JsMethod = "abs")]
[assembly: MapMethod(typeof(Math), nameof(Math.Min), JsMethod = "min")]
[assembly: MapMethod(typeof(Math), nameof(Math.Max), JsMethod = "max")]
[assembly: MapMethod(typeof(Math), nameof(Math.Sqrt), JsMethod = "sqrt")]
[assembly: MapMethod(typeof(Math), nameof(Math.Pow), JsMethod = "pow")]
