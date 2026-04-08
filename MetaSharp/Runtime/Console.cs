// Declarative BCL → JavaScript mappings for System.Console.
//
// JS exposes a global lowercase `console` object whose method names differ from C#
// (WriteLine → log, etc.), so we use the JsTemplate form to substitute the receiver
// with the lowercase identifier instead of the simple-rename shorthand (which would
// preserve the PascalCase Console).

using System;
using MetaSharp.Annotations;

[assembly: MapMethod(typeof(Console), nameof(Console.WriteLine), JsTemplate = "console.log($0)")]
