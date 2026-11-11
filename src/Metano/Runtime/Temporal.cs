// Declarative BCL → JavaScript mappings for the System.* date/time APIs.
//
// DateTime / DateOnly / DateTimeOffset / TimeSpan all lower to the JS Temporal proposal
// (currently the @js-temporal/polyfill); the type-level mapping (DateOnly → PlainDate,
// etc.) is handled by TypeMapper. This file declares the runtime *member* lowerings —
// the static factories and the helper extensions used by the transpiled code at the
// call site.

using Metano.Annotations;

// DateTimeOffset.UtcNow → Temporal.Now.zonedDateTimeISO()
[assembly: MapProperty(
    typeof(DateTimeOffset),
    nameof(DateTimeOffset.UtcNow),
    JsTemplate = "Temporal.Now.zonedDateTimeISO()"
)]

// DateOnly.DayNumber → dayNumber($this)
// Lowers to a call into the metano-runtime dayNumber helper, since Temporal.PlainDate
// has no equivalent property. The RuntimeImports annotation tells the import collector
// to emit `import { dayNumber } from "metano-runtime";` — without it the identifier
// would be invisible to the AST walker because it lives inside the opaque template body.
[assembly: MapProperty(
    typeof(DateOnly),
    nameof(DateOnly.DayNumber),
    JsTemplate = "dayNumber($this)",
    RuntimeImports = "dayNumber"
)]

// Ticks (spec 012 FR-042): one tick is 100 ns, and `DateTime` ticks count from
// 0001-01-01T00:00:00. The 64-bit tick counts are `bigint` values; the
// `TimeSpan.TicksPer*` constants fold.
[assembly: MapProperty(
    typeof(TimeSpan),
    nameof(TimeSpan.Ticks),
    JsTemplate = "durationTicks($this)",
    RuntimeImports = "durationTicks"
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromTicks),
    JsTemplate = "durationFromTicks($0)",
    RuntimeImports = "durationFromTicks",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "durationFromTicks($0)",
    RuntimeImports = "durationFromTicks",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapProperty(
    typeof(DateTime),
    nameof(DateTime.Ticks),
    JsTemplate = "plainDateTimeTicks($this)",
    RuntimeImports = "plainDateTimeTicks"
)]
[assembly: MapMethod(
    typeof(DateTime),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainDateTimeFromTicks($0)",
    RuntimeImports = "plainDateTimeFromTicks",
    WhenParameterTypes = [typeof(long)]
)]

// Constructors from components (#651): the runtime factories check .NET's ranges and throw
// its `ArgumentOutOfRangeException`, where the Temporal constructors throw a `RangeError` (or
// accept year 0) that a `catch (ArgumentOutOfRangeException)` would not take.
[assembly: MapMethod(
    typeof(DateOnly),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainDate($0, $1, $2)",
    RuntimeImports = "plainDate",
    WhenParameterTypes = [typeof(int), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(TimeOnly),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainTime($0, $1)",
    RuntimeImports = "plainTime",
    WhenParameterTypes = [typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(TimeOnly),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainTime($0, $1, $2)",
    RuntimeImports = "plainTime",
    WhenParameterTypes = [typeof(int), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(TimeOnly),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainTime($0, $1, $2, $3)",
    RuntimeImports = "plainTime",
    WhenParameterTypes = [typeof(int), typeof(int), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(DateTime),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainDateTime($0, $1, $2)",
    RuntimeImports = "plainDateTime",
    WhenParameterTypes = [typeof(int), typeof(int), typeof(int)]
)]
[assembly: MapMethod(
    typeof(DateTime),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainDateTime($0, $1, $2, $3, $4, $5)",
    RuntimeImports = "plainDateTime",
    WhenParameterTypes = [
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
    ]
)]
[assembly: MapMethod(
    typeof(DateTime),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "plainDateTime($0, $1, $2, $3, $4, $5, $6)",
    RuntimeImports = "plainDateTime",
    WhenParameterTypes = [
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
    ]
)]
