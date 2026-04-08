// Declarative BCL → JavaScript mappings for System.String.
//
// Most are straightforward renames between the C# method and the JS String prototype
// equivalent. Methods absent from the JS string prototype (PadLeft/PadRight, Format, etc.)
// are not mapped here yet — they'll need either templates or runtime helpers.

using MetaSharp.Annotations;

// ─── string property ────────────────────────────────────────

[assembly: MapProperty(typeof(string), nameof(string.Length), JsProperty = "length")]

// ─── Case conversion ────────────────────────────────────────
// JS string has no locale variants; both invariant and culture-aware C# helpers map to
// the same JS counterpart.

[assembly: MapMethod(typeof(string), nameof(string.ToUpper), JsMethod = "toUpperCase")]
[assembly: MapMethod(typeof(string), nameof(string.ToUpperInvariant), JsMethod = "toUpperCase")]
[assembly: MapMethod(typeof(string), nameof(string.ToLower), JsMethod = "toLowerCase")]
[assembly: MapMethod(typeof(string), nameof(string.ToLowerInvariant), JsMethod = "toLowerCase")]

// ─── Search / inspection ────────────────────────────────────

[assembly: MapMethod(typeof(string), nameof(string.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(string), nameof(string.StartsWith), JsMethod = "startsWith")]
[assembly: MapMethod(typeof(string), nameof(string.EndsWith), JsMethod = "endsWith")]
[assembly: MapMethod(typeof(string), nameof(string.IndexOf), JsMethod = "indexOf")]

// ─── Trimming ───────────────────────────────────────────────

[assembly: MapMethod(typeof(string), nameof(string.Trim), JsMethod = "trim")]
[assembly: MapMethod(typeof(string), nameof(string.TrimStart), JsMethod = "trimStart")]
[assembly: MapMethod(typeof(string), nameof(string.TrimEnd), JsMethod = "trimEnd")]

// ─── Substring / replace ────────────────────────────────────

[assembly: MapMethod(typeof(string), nameof(string.Substring), JsMethod = "substring")]
[assembly: MapMethod(typeof(string), nameof(string.Replace), JsMethod = "replace")]
