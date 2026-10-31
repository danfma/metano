// Default BCL → JavaScript mapping for `System.Decimal`. Lowers to the `Decimal` class
// from the `decimal.js` package — a precise arbitrary-precision number type that
// preserves the C# `decimal` semantics (no IEEE-754 rounding errors). Without this
// mapping, `decimal` would silently fall through to the primitive switch and become
// the JS `number` primitive, which is a footgun for any monetary or scientific code.
//
// **Consumer requirement:** add `decimal.js` to your `package.json` dependencies.
// Metano does NOT manage the JS package install — only the import statement in the
// generated TypeScript points at the package.
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
    FromPackage = "decimal.js",
    Version = "^10.6.0"
)]

// ─── Constants ──────────────────────────────────────────────
// decimal.js has no static `Zero` / `One` — we wrap each access in a fresh Decimal.
// For high-allocation hot paths the user can hoist their own constant; this default
// just keeps the call site syntactically equivalent.

[assembly: MapProperty(typeof(decimal), nameof(decimal.Zero), JsTemplate = "new Decimal(0)")]
[assembly: MapProperty(typeof(decimal), nameof(decimal.One), JsTemplate = "new Decimal(1)")]
[assembly: MapProperty(typeof(decimal), nameof(decimal.MinusOne), JsTemplate = "new Decimal(-1)")]

// ─── Parsing ────────────────────────────────────────────────
// decimal.Parse(s) → new Decimal(s). decimal.js's constructor accepts strings directly
// and throws on malformed input, matching the FormatException semantics.

[assembly: MapMethod(typeof(decimal), nameof(decimal.Parse), JsTemplate = "new Decimal($0)")]

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
