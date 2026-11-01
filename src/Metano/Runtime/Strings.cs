// Declarative BCL → JavaScript mappings for System.String.
//
// Most are straightforward renames between the C# method and the JS String prototype
// equivalent; members whose C# semantics differ go through metano-runtime helpers.
// Methods absent from the JS string prototype (PadLeft/PadRight, Format, etc.) are not
// mapped here yet — they'll need either templates or runtime helpers.

using Metano.Annotations;

// ─── string property ────────────────────────────────────────

[assembly: MapProperty(typeof(string), nameof(string.Length), JsProperty = "length")]
[assembly: MapProperty(typeof(string), nameof(string.Empty), JsTemplate = "\"\"")]

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

// s.Replace(old, new) → stringReplace(s, old, new) — runtime helper from metano-runtime.
// C# replaces every occurrence and inserts `new` literally; JS `replace` stops at the
// first occurrence, and both `replace` and `replaceAll` expand `$&`-style sequences in
// the replacement. The helper also keeps C#'s argument checks (an empty `old` throws)
// and treats a null `new` as removal. Covers the (char, char) and (string, string?)
// overloads; the culture- and comparison-aware overloads stay unmapped.

[assembly: MapMethod(
    typeof(string),
    nameof(string.Replace),
    JsTemplate = "stringReplace($this, $0, $1)",
    RuntimeImports = "stringReplace",
    WhenArgCount = 2
)]

// ─── Null / empty checks ────────────────────────────────────
// string.IsNullOrEmpty(s) / string.IsNullOrWhiteSpace(s) → runtime helpers that read
// the argument once. IsNullOrWhiteSpace tests .NET's white-space set, which differs
// from JS `trim()` on U+0085 and U+FEFF.

[assembly: MapMethod(
    typeof(string),
    nameof(string.IsNullOrEmpty),
    JsTemplate = "isNullOrEmpty($0)",
    RuntimeImports = "isNullOrEmpty"
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IsNullOrWhiteSpace),
    JsTemplate = "isNullOrWhiteSpace($0)",
    RuntimeImports = "isNullOrWhiteSpace"
)]

// ─── Join ───────────────────────────────────────────────────
// `string.Join(separator, values)` → `values.join(separator)`. The extractor gathers the
// values into one array argument first: expanded `params` arguments become an array
// literal, and an enumerable that does not lower to a JS array is spread into one
// (`[...values]`). JS `join` writes `null` / `undefined` elements as empty strings, as
// C# does. The `(separator, value, startIndex, count)` overloads stay unmapped.

[assembly: MapMethod(
    typeof(string),
    nameof(string.Join),
    JsTemplate = "$1.join($0)",
    WhenArgCount = 2
)]
