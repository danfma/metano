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
