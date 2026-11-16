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

// TimeSpan components (#673): `new TimeSpan(0, 5, 0)` is five minutes, where
// `new Temporal.Duration(0, 5, 0)` reads its arguments as years, months and weeks. The runtime
// builds the duration from the component sum and throws .NET's `ArgumentOutOfRangeException`
// past `TimeSpan.MaxValue`. A constructor that takes components has no `long` overload, so
// the argument count picks the template; the `(long ticks)` constructor is mapped above.
[assembly: MapMethod(
    typeof(TimeSpan),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "timeSpanFromParts(0, $0, $1, $2)",
    RuntimeImports = "timeSpanFromParts",
    WhenArgCount = 3
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "timeSpanFromParts($0, $1, $2, $3)",
    RuntimeImports = "timeSpanFromParts",
    WhenArgCount = 4
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "timeSpanFromParts($0, $1, $2, $3, $4)",
    RuntimeImports = "timeSpanFromParts",
    WhenArgCount = 5
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "timeSpanFromParts($0, $1, $2, $3, $4, $5)",
    RuntimeImports = "timeSpanFromParts",
    WhenArgCount = 6
)]

// TimeSpan.Zero, and the single-argument FromDays / FromHours / FromMinutes / FromSeconds /
// FromMilliseconds / FromMicroseconds (the `double`, `int` and `long` overloads): the runtime
// scales in ticks, truncates a fraction toward zero as .NET does, and throws its
// `ArgumentException` (NaN), `OverflowException` and `ArgumentOutOfRangeException`. The
// .NET 9 overloads that combine several units (`FromHours(2, 30)`) stay unmapped (MS0001).
[assembly: MapProperty(
    typeof(TimeSpan),
    nameof(TimeSpan.Zero),
    JsTemplate = "timeSpanZero",
    RuntimeImports = "timeSpanZero"
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromDays),
    JsTemplate = "timeSpanFromDays($0)",
    RuntimeImports = "timeSpanFromDays",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromHours),
    JsTemplate = "timeSpanFromHours($0)",
    RuntimeImports = "timeSpanFromHours",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromMinutes),
    JsTemplate = "timeSpanFromMinutes($0)",
    RuntimeImports = "timeSpanFromMinutes",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromSeconds),
    JsTemplate = "timeSpanFromSeconds($0)",
    RuntimeImports = "timeSpanFromSeconds",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromMilliseconds),
    JsTemplate = "timeSpanFromMilliseconds($0)",
    RuntimeImports = "timeSpanFromMilliseconds",
    WhenArgCount = 1
)]
[assembly: MapMethod(
    typeof(TimeSpan),
    nameof(TimeSpan.FromMicroseconds),
    JsTemplate = "timeSpanFromMicroseconds($0)",
    RuntimeImports = "timeSpanFromMicroseconds",
    WhenArgCount = 1
)]

// DateTimeOffset constructors (#673): a `Temporal.ZonedDateTime` at a fixed offset. The runtime
// checks .NET's ranges and the offset (whole minutes, within fourteen hours) and throws its
// exceptions. The `(DateTime, TimeSpan)` form cannot check the `DateTime` kind: the
// `Temporal.PlainDateTime` carries none.
[assembly: MapMethod(
    typeof(DateTimeOffset),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "dateTimeOffset($0, $1, $2, $3, $4, $5, 0, $6)",
    RuntimeImports = "dateTimeOffset",
    WhenArgCount = 7
)]
[assembly: MapMethod(
    typeof(DateTimeOffset),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "dateTimeOffset($0, $1, $2, $3, $4, $5, $6, $7)",
    RuntimeImports = "dateTimeOffset",
    WhenArgCount = 8
)]
[assembly: MapMethod(
    typeof(DateTimeOffset),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "dateTimeOffsetFromDateTime($0, $1)",
    RuntimeImports = "dateTimeOffsetFromDateTime",
    WhenParameterTypes = [typeof(DateTime), typeof(TimeSpan)]
)]
[assembly: MapMethod(
    typeof(DateTimeOffset),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "dateTimeOffsetFromTicks($0, $1)",
    RuntimeImports = "dateTimeOffsetFromTicks",
    WhenParameterTypes = [typeof(long), typeof(TimeSpan)]
)]
