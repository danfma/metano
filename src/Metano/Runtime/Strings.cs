// Declarative BCL → JavaScript mappings for System.String.
//
// Most are straightforward renames between the C# method and the JS String prototype
// equivalent; members whose C# semantics differ go through metano-runtime helpers.
// Members absent from the JS string prototype, or whose arguments it reads differently
// (Substring, Remove, Insert, PadLeft/PadRight, Split, ...), go through metano-runtime
// helpers. Format and the other unlisted members are not mapped yet.

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

// The overloads that take a `StringComparison`, a start index or a count go through
// `metano-runtime` helpers (#661): JavaScript reads the second argument of `includes`,
// `startsWith`, `endsWith` and `indexOf` as a position, so the enum value would be taken for
// one, and `indexOf` ignores a third. The comparison-aware helpers compare ordinally (the
// culture-sensitive comparisons behave as their ordinal counterparts) and the case-insensitive
// ones fold each UTF-16 code unit by its invariant upper-case form. `LastIndexOf` with a start
// index searches backwards from it (a match must end at or before it), which JavaScript's
// `lastIndexOf` does not. The helpers throw .NET's `ArgumentNullException` and
// `ArgumentOutOfRangeException`. A call that is not listed keeps the by-name rename below.

[assembly: ExportFromBcl(
    typeof(StringComparison),
    ExportedName = "StringComparison",
    FromPackage = "metano-runtime"
)]
[assembly: ExportFromBcl(
    typeof(StringSplitOptions),
    ExportedName = "StringSplitOptions",
    FromPackage = "metano-runtime"
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Contains),
    JsTemplate = "stringContains($this, $0, $1)",
    RuntimeImports = "stringContains",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Contains),
    JsTemplate = "stringContains($this, $0, $1)",
    RuntimeImports = "stringContains",
    WhenParameterTypes = [typeof(char), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.StartsWith),
    JsTemplate = "stringStartsWith($this, $0, $1)",
    RuntimeImports = "stringStartsWith",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.EndsWith),
    JsTemplate = "stringEndsWith($this, $0, $1)",
    RuntimeImports = "stringEndsWith",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(char), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1, $2)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1, $2)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(char), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, 0, undefined, $1)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1, undefined, $2)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.IndexOf),
    JsTemplate = "stringIndexOf($this, $0, $1, $2, $3)",
    RuntimeImports = "stringIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(int), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(char), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1, $2)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1, $2)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(char), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, undefined, undefined, $1)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1, undefined, $2)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.LastIndexOf),
    JsTemplate = "stringLastIndexOf($this, $0, $1, $2, $3)",
    RuntimeImports = "stringLastIndexOf",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(int), typeof(StringComparison)]
)]

[assembly: MapMethod(typeof(string), nameof(string.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(string), nameof(string.StartsWith), JsMethod = "startsWith")]
[assembly: MapMethod(typeof(string), nameof(string.EndsWith), JsMethod = "endsWith")]
[assembly: MapMethod(typeof(string), nameof(string.IndexOf), JsMethod = "indexOf")]

// ─── Trimming ───────────────────────────────────────────────

// `Trim(char)` / `Trim(char[])` (and the `TrimStart` / `TrimEnd` forms) remove those
// characters; JavaScript's `trim` takes no argument and would trim white space instead
// (#661). The no-argument forms keep the JS rename: its white-space set differs from .NET's
// only on U+0085 and U+FEFF (see `isNullOrWhiteSpace`).

[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1, $2)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1, $2)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Trim),
    JsTemplate = "stringTrim($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrim",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1, $2)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1, $2)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimStart),
    JsTemplate = "stringTrimStart($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrimStart",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(char)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1, $2)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1, $2)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.TrimEnd),
    JsTemplate = "stringTrimEnd($this, $0, $1, $2, $3)",
    RuntimeImports = "stringTrimEnd",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 4
)]

[assembly: MapMethod(typeof(string), nameof(string.Trim), JsMethod = "trim")]
[assembly: MapMethod(typeof(string), nameof(string.TrimStart), JsMethod = "trimStart")]
[assembly: MapMethod(typeof(string), nameof(string.TrimEnd), JsMethod = "trimEnd")]

// ─── Substring / replace ────────────────────────────────────

// Substring(start, length) → stringSubstring(s, start, length). C# takes a length where
// JavaScript's `substring` takes an end index, and `substring` swaps and clamps bad
// arguments where C# throws `ArgumentOutOfRangeException` (#661). `Remove`, `Insert`,
// `PadLeft` / `PadRight` and `ToCharArray` have no JavaScript namesake; the helpers keep
// C#'s argument checks and read each argument once.

[assembly: MapMethod(
    typeof(string),
    nameof(string.Substring),
    JsTemplate = "stringSubstring($this, $0)",
    RuntimeImports = "stringSubstring",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Substring),
    JsTemplate = "stringSubstring($this, $0, $1)",
    RuntimeImports = "stringSubstring",
    WhenParameterTypes = [typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Remove),
    JsTemplate = "stringRemove($this, $0)",
    RuntimeImports = "stringRemove",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Remove),
    JsTemplate = "stringRemove($this, $0, $1)",
    RuntimeImports = "stringRemove",
    WhenParameterTypes = [typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Insert),
    JsTemplate = "stringInsert($this, $0, $1)",
    RuntimeImports = "stringInsert",
    WhenParameterTypes = [typeof(int), typeof(string)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.PadLeft),
    JsTemplate = "stringPadLeft($this, $0)",
    RuntimeImports = "stringPadLeft",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.PadLeft),
    JsTemplate = "stringPadLeft($this, $0, $1)",
    RuntimeImports = "stringPadLeft",
    WhenParameterTypes = [typeof(int), typeof(char)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.PadRight),
    JsTemplate = "stringPadRight($this, $0)",
    RuntimeImports = "stringPadRight",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.PadRight),
    JsTemplate = "stringPadRight($this, $0, $1)",
    RuntimeImports = "stringPadRight",
    WhenParameterTypes = [typeof(int), typeof(char)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.ToCharArray),
    JsTemplate = "stringToCharArray($this)",
    RuntimeImports = "stringToCharArray",
    WhenParameterTypes = []
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.ToCharArray),
    JsTemplate = "stringToCharArray($this, $0, $1)",
    RuntimeImports = "stringToCharArray",
    WhenParameterTypes = [typeof(int), typeof(int)]
)]

// Split → stringSplit(s, separator, isCharArray, count, options). JavaScript's
// `split(separator, limit)` drops the pieces past the limit where C# keeps the rest of the
// string in the last one, and it has no options, no separator set and no white-space form
// (#661). The third argument marks the array overloads, whose missing or empty list splits at
// white space; `Split(params char[])` takes up to four expanded characters (more stay by name).

[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1, $2)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1, $2)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1, $2, $3)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(char[])],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplitChars($this, $0, $1, $2, $3)",
    RuntimeImports = "stringSplitChars",
    WhenParameterTypes = [typeof(ReadOnlySpan<char>)],
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char), typeof(StringSplitOptions)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char), typeof(StringSplitOptions)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char), typeof(int), typeof(StringSplitOptions)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char), typeof(int), typeof(StringSplitOptions)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(int), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string), typeof(StringSplitOptions)],
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string), typeof(StringSplitOptions)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(StringSplitOptions)],
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, false, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string), typeof(int), typeof(StringSplitOptions)],
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string[]), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string[]), typeof(int), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(int)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(char[]), typeof(int), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, undefined, $1)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string[]), typeof(StringSplitOptions)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Split),
    JsTemplate = "stringSplit($this, $0, true, $1, $2)",
    RuntimeImports = "stringSplit",
    WhenParameterTypes = [typeof(string[]), typeof(int), typeof(StringSplitOptions)]
)]

// s.Replace(old, new) → stringReplace(s, old, new) — runtime helper from metano-runtime.
// C# replaces every occurrence and inserts `new` literally; JS `replace` stops at the
// first occurrence, and both `replace` and `replaceAll` expand `$&`-style sequences in
// the replacement. The helper also keeps C#'s argument checks (an empty `old` throws)
// and treats a null `new` as removal. Covers the (char, char) and (string, string?)
// overloads, the StringComparison one (a case-insensitive comparison matches `old` in
// any case; the culture members match as their ordinal counterparts) and the
// (string, string?, bool ignoreCase, CultureInfo?) one, whose culture is not read.

[assembly: MapMethod(
    typeof(string),
    nameof(string.Replace),
    JsTemplate = "stringReplace($this, $0, $1)",
    RuntimeImports = "stringReplace",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Replace),
    JsTemplate = "stringReplace($this, $0, $1, $2)",
    RuntimeImports = "stringReplace",
    WhenParameterTypes = [typeof(string), typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Replace),
    JsTemplate = "stringReplace($this, $0, $1, $2 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)",
    RuntimeImports = "stringReplace, StringComparison",
    WhenArgCount = 4
)]

// ─── Equality and ordering ──────────────────────────────────
// string.Equals / string.Compare with a StringComparison, and the overloads that imply
// one, go through the `stringEquals` / `stringCompare` runtime helpers (#769): JavaScript
// strings have no `equals` / `compareTo`, the case-insensitive forms fold each code point
// by .NET's ordinal uppercase, and the culture forms compare with `Intl.Collator`.
// `Compare(a, b)`, `Compare(a, b, ignoreCase)` and `CompareTo` are culture-sensitive
// (CurrentCulture), `CompareOrdinal` is ordinal. The plain `Equals` forms are ordinal, so
// they compare with `===`.

[assembly: MapMethod(
    typeof(string),
    nameof(string.Equals),
    JsTemplate = "stringEquals($0, $1, $2)",
    RuntimeImports = "stringEquals",
    WhenParameterTypes = [typeof(string), typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Equals),
    JsTemplate = "stringEquals($this, $0, $1)",
    RuntimeImports = "stringEquals",
    WhenParameterTypes = [typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Equals),
    JsTemplate = "($0 === $1)",
    WhenParameterTypes = [typeof(string), typeof(string)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Equals),
    JsTemplate = "($this === $0)",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Equals),
    JsTemplate = "($this === $0)",
    WhenParameterTypes = [typeof(object)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Compare),
    JsTemplate = "stringCompare($0, $1, $2)",
    RuntimeImports = "stringCompare",
    WhenParameterTypes = [typeof(string), typeof(string), typeof(StringComparison)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Compare),
    JsTemplate = "stringCompare($0, $1, StringComparison.CurrentCulture)",
    RuntimeImports = "stringCompare, StringComparison",
    WhenParameterTypes = [typeof(string), typeof(string)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.Compare),
    JsTemplate = "stringCompare($0, $1, $2 ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture)",
    RuntimeImports = "stringCompare, StringComparison",
    WhenParameterTypes = [typeof(string), typeof(string), typeof(bool)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.CompareOrdinal),
    JsTemplate = "stringCompare($0, $1, StringComparison.Ordinal)",
    RuntimeImports = "stringCompare, StringComparison",
    WhenParameterTypes = [typeof(string), typeof(string)]
)]
[assembly: MapMethod(
    typeof(string),
    nameof(string.CompareTo),
    JsTemplate = "stringCompare($this, $0, StringComparison.CurrentCulture)",
    RuntimeImports = "stringCompare, StringComparison",
    WhenParameterTypes = [typeof(string)]
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

// string.Join(separator, values, startIndex, count) → stringJoinRange(...): joins `count`
// strings from `startIndex` and checks the range as C# does (#661).
[assembly: MapMethod(
    typeof(string),
    nameof(string.Join),
    JsTemplate = "stringJoinRange($0, $1, $2, $3)",
    RuntimeImports = "stringJoinRange",
    WhenArgCount = 4
)]
