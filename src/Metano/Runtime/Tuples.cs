// Declarative BCL → JavaScript mappings for the reference tuple family (System.Tuple).
//
// A System.Tuple<T1, …> is a positional JS array at runtime (the same `[a, b]` shape as
// a value tuple), so construction builds the array, asserted to its tuple type so a
// local keeps per-position types, and ItemN reads position N - 1.
//
// A C# value tuple is the same positional array in TypeScript (the bridge lowers its
// literals and positional element reads); its instance `Equals` compares element-wise,
// the contract of the runtime `valueEquals` over arrays.

using Metano.Annotations;

// ─── Arity 1 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0] as [$T0]"
)]
[assembly: MapProperty(typeof(Tuple<>), nameof(Tuple<int>.Item1), JsTemplate = "$this[0]")]

// ─── Arity 2 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1] as [$T0, $T1]"
)]
[assembly: MapProperty(typeof(Tuple<,>), nameof(Tuple<int, int>.Item1), JsTemplate = "$this[0]")]
[assembly: MapProperty(typeof(Tuple<,>), nameof(Tuple<int, int>.Item2), JsTemplate = "$this[1]")]

// ─── Arity 3 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1, $2] as [$T0, $T1, $T2]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,>),
    nameof(Tuple<int, int, int>.Item1),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,>),
    nameof(Tuple<int, int, int>.Item2),
    JsTemplate = "$this[1]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,>),
    nameof(Tuple<int, int, int>.Item3),
    JsTemplate = "$this[2]"
)]

// ─── Arity 4 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,,,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1, $2, $3] as [$T0, $T1, $T2, $T3]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,>),
    nameof(Tuple<int, int, int, int>.Item1),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,>),
    nameof(Tuple<int, int, int, int>.Item2),
    JsTemplate = "$this[1]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,>),
    nameof(Tuple<int, int, int, int>.Item3),
    JsTemplate = "$this[2]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,>),
    nameof(Tuple<int, int, int, int>.Item4),
    JsTemplate = "$this[3]"
)]

// ─── Arity 5 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,,,,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1, $2, $3, $4] as [$T0, $T1, $T2, $T3, $T4]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,>),
    nameof(Tuple<int, int, int, int, int>.Item1),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,>),
    nameof(Tuple<int, int, int, int, int>.Item2),
    JsTemplate = "$this[1]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,>),
    nameof(Tuple<int, int, int, int, int>.Item3),
    JsTemplate = "$this[2]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,>),
    nameof(Tuple<int, int, int, int, int>.Item4),
    JsTemplate = "$this[3]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,>),
    nameof(Tuple<int, int, int, int, int>.Item5),
    JsTemplate = "$this[4]"
)]

// ─── Arity 6 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,,,,,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1, $2, $3, $4, $5] as [$T0, $T1, $T2, $T3, $T4, $T5]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item1),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item2),
    JsTemplate = "$this[1]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item3),
    JsTemplate = "$this[2]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item4),
    JsTemplate = "$this[3]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item5),
    JsTemplate = "$this[4]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,>),
    nameof(Tuple<int, int, int, int, int, int>.Item6),
    JsTemplate = "$this[5]"
)]

// ─── Arity 7 ──────────────────────────────────────────────

[assembly: MapMethod(
    typeof(Tuple<,,,,,,>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "[$0, $1, $2, $3, $4, $5, $6] as [$T0, $T1, $T2, $T3, $T4, $T5, $T6]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item1),
    JsTemplate = "$this[0]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item2),
    JsTemplate = "$this[1]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item3),
    JsTemplate = "$this[2]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item4),
    JsTemplate = "$this[3]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item5),
    JsTemplate = "$this[4]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item6),
    JsTemplate = "$this[5]"
)]
[assembly: MapProperty(
    typeof(Tuple<,,,,,,>),
    nameof(Tuple<int, int, int, int, int, int, int>.Item7),
    JsTemplate = "$this[6]"
)]

// ─── ValueTuple equality ──────────────────────────────────

[assembly: MapMethod(
    typeof(ValueTuple<>),
    nameof(ValueTuple<int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,>),
    nameof(ValueTuple<int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,>),
    nameof(ValueTuple<int, int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,>),
    nameof(ValueTuple<int, int, int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,>),
    nameof(ValueTuple<int, int, int, int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,,>),
    nameof(ValueTuple<int, int, int, int, int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,,,>),
    nameof(ValueTuple<int, int, int, int, int, int, int>.Equals),
    JsTemplate = "valueEquals($this, $0)",
    RuntimeImports = "valueEquals",
    WhenArgCount = 1
)]
