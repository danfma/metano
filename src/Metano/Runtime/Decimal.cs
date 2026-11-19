// Default BCL → JavaScript mapping for `System.Decimal`. Lowers to the `Decimal` class
// of `metano-runtime/system/decimal` — decimal.js, a precise arbitrary-precision number type
// that preserves the C# `decimal` semantics (no IEEE-754 rounding errors), configured as a
// clone with `System.Decimal`'s 29 significant digits and its half-to-even rounding instead of
// decimal.js's defaults of 20 digits and half up (#465). Without this mapping, `decimal`
// would silently fall through to the primitive switch and become the JS `number` primitive,
// which is a footgun for any monetary or scientific code.
//
// decimal.js is a dependency of `metano-runtime`, so a consumer no longer lists it. A project
// that declares its own `[ExportFromBcl(typeof(decimal), FromPackage = "decimal.js", ...)]`
// keeps the unconfigured class.
//
// This file declares the **type-level** mapping plus member-level mappings (constants,
// parsing, conversions, comparison). Literal lowering (1.5m → new Decimal("1.5")) and
// operator lowering (a + b → a.plus(b) for decimal operands) live inside the compiler
// itself, since they need access to the semantic model — see LiteralHandler and
// OperatorHandler under Metano.Compiler.TypeScript/Transformation.

using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(decimal),
    ExportedName = "Decimal",
    FromPackage = "metano-runtime/system/decimal"
)]

// ─── Constants ──────────────────────────────────────────────
// decimal.js has no static `Zero` / `One` — we wrap each access in a fresh Decimal.
// For high-allocation hot paths the user can hoist their own constant; this default
// just keeps the call site syntactically equivalent.

[assembly: MapProperty(typeof(decimal), nameof(decimal.Zero), JsTemplate = "new $Type(0)")]
[assembly: MapProperty(typeof(decimal), nameof(decimal.One), JsTemplate = "new $Type(1)")]
[assembly: MapProperty(typeof(decimal), nameof(decimal.MinusOne), JsTemplate = "new $Type(-1)")]

// ─── Parsing ────────────────────────────────────────────────
// decimal.Parse(s) → parseDecimal(s, $Type), also for decimal.Parse(s, provider), which
// the extractor binds to this overload. The helper keeps presentation scale out of band.
// The NumberStyles / span overloads keep new Decimal(s).
// The runtime helper checks .NET's
// `NumberStyles.Number` grammar and range and throws FormatException / OverflowException /
// ArgumentNullException as .NET does; decimal.js's own `DecimalError` would escape a
// `catch (FormatException)`.

[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.Parse),
    WhenParameterTypes = [typeof(string)],
    JsTemplate = "parseDecimal($0, $Type)",
    RuntimeImports = "parseDecimal"
)]

[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.ToString),
    JsTemplate = "formatText($this)",
    RuntimeImports = "formatText",
    WhenArgCount = 0
)]

// decimal.TryParse(s, out d) → tryParseDecimal(s, Decimal), which returns `[found, value]` (the
// `out` slot holds `0m` and is not read). The helper applies the grammar of `decimal.Parse`
// and reports a failure, `null` included, as `[false, 0]` (#673). The `Decimal` class goes in
// as an argument so the runtime never imports decimal.js.

[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.TryParse),
    WhenParameterTypes = [typeof(string), typeof(decimal)],
    JsTemplate = "tryParseDecimal($0, Decimal)",
    RuntimeImports = "tryParseDecimal"
)]
[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.TryParse),
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider), typeof(decimal)],
    JsTemplate = "tryParseDecimal($0, Decimal)",
    RuntimeImports = "tryParseDecimal"
)]

// ─── Comparison / conversion ────────────────────────────────
// d.CompareTo(other) → d.cmp(other) — decimal.js returns -1/0/1 just like
// IComparable.CompareTo, so the rename is enough.

[assembly: MapMethod(typeof(decimal), nameof(decimal.CompareTo), JsMethod = "cmp")]

// ─── Rounding ───────────────────────────────────────────────
// decimal.js `round()` rounds a midpoint away from zero by default, while C# rounds it
// to the nearest even number. Every overload lowers to the runtime
// `roundDecimal(value, digits?, mode?)` helper, which calls `toDecimalPlaces` with the
// matching decimal.js rounding constant. The extractor rewrites the `Math.Round(decimal…)`
// overloads to `decimal.Round` and normalizes `(value, mode)` to `(value, 0, mode)`, so
// the argument count alone picks the template.

[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.Round),
    JsTemplate = "roundDecimal($0)",
    RuntimeImports = "roundDecimal",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.Round),
    JsTemplate = "roundDecimal($0, $1)",
    RuntimeImports = "roundDecimal",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(decimal),
    nameof(decimal.Round),
    JsTemplate = "roundDecimal($0, $1, $2)",
    RuntimeImports = "roundDecimal",
    WhenArgCount = 3
)]
