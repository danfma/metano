// Declarative BCL → JavaScript mappings for the floating-point types System.Double and
// System.Single.
//
// Both lower to the JS `number` type, so the classification helpers map onto the
// global `Number`/`Math`/`Object` functions and the special values onto JS literals.
//
// Template shape is deliberate. Templates are substituted as text, so every template
// either keeps each placeholder in a call-argument position (safe whatever the
// argument is) or wraps itself in parentheses (safe whatever surrounds it):
//   - `Object.is($0, Infinity)` instead of `$0 === Infinity`: an argument such as
//     `a ?? b` would otherwise bind as `a ?? (b === Infinity)`. Object.is differs from
//     `===` only for NaN and ±0, neither of which can equal ±Infinity.
//   - `(Math.abs($0) === Infinity)` evaluates the argument once, unlike
//     `$0 === Infinity || $0 === -Infinity`.
//   - Negative constants use a named property or parentheses so a unary minus at the
//     call site cannot glue into `--`.
//
// System.Single values are JS doubles at run time, so float constants are written as
// the exact double value of the corresponding float.

using Metano.Annotations;

// ── System.Double ─────────────────────────────────────────────────────────

[assembly: MapMethod(typeof(double), nameof(double.IsNaN), JsTemplate = "Number.isNaN($0)")]
[assembly: MapMethod(typeof(double), nameof(double.IsFinite), JsTemplate = "Number.isFinite($0)")]

[assembly: MapMethod(
    typeof(double),
    nameof(double.IsInfinity),
    JsTemplate = "(Math.abs($0) === Infinity)"
)]

[assembly: MapMethod(
    typeof(double),
    nameof(double.IsPositiveInfinity),
    JsTemplate = "Object.is($0, Infinity)"
)]

[assembly: MapMethod(
    typeof(double),
    nameof(double.IsNegativeInfinity),
    JsTemplate = "Object.is($0, -Infinity)"
)]

[assembly: MapProperty(typeof(double), nameof(double.NaN), JsTemplate = "NaN")]
[assembly: MapProperty(typeof(double), nameof(double.PositiveInfinity), JsTemplate = "Infinity")]

[assembly: MapProperty(
    typeof(double),
    nameof(double.NegativeInfinity),
    JsTemplate = "Number.NEGATIVE_INFINITY"
)]

[assembly: MapProperty(typeof(double), nameof(double.Epsilon), JsTemplate = "Number.MIN_VALUE")]
[assembly: MapProperty(typeof(double), nameof(double.MaxValue), JsTemplate = "Number.MAX_VALUE")]
[assembly: MapProperty(typeof(double), nameof(double.MinValue), JsTemplate = "(-Number.MAX_VALUE)")]

// ── System.Single ─────────────────────────────────────────────────────────

[assembly: MapMethod(typeof(float), nameof(float.IsNaN), JsTemplate = "Number.isNaN($0)")]
[assembly: MapMethod(typeof(float), nameof(float.IsFinite), JsTemplate = "Number.isFinite($0)")]

[assembly: MapMethod(
    typeof(float),
    nameof(float.IsInfinity),
    JsTemplate = "(Math.abs($0) === Infinity)"
)]

[assembly: MapMethod(
    typeof(float),
    nameof(float.IsPositiveInfinity),
    JsTemplate = "Object.is($0, Infinity)"
)]

[assembly: MapMethod(
    typeof(float),
    nameof(float.IsNegativeInfinity),
    JsTemplate = "Object.is($0, -Infinity)"
)]

[assembly: MapProperty(typeof(float), nameof(float.NaN), JsTemplate = "NaN")]
[assembly: MapProperty(typeof(float), nameof(float.PositiveInfinity), JsTemplate = "Infinity")]

[assembly: MapProperty(
    typeof(float),
    nameof(float.NegativeInfinity),
    JsTemplate = "Number.NEGATIVE_INFINITY"
)]

[assembly: MapProperty(typeof(float), nameof(float.Epsilon), JsTemplate = "1.401298464324817e-45")]

[assembly: MapProperty(
    typeof(float),
    nameof(float.MaxValue),
    JsTemplate = "3.4028234663852886e+38"
)]

[assembly: MapProperty(
    typeof(float),
    nameof(float.MinValue),
    JsTemplate = "(-3.4028234663852886e+38)"
)]
