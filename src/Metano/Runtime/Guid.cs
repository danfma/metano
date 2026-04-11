// Declarative BCL → JavaScript mappings for System.Guid.
//
// Guid maps to the branded `UUID` type shipped by metano-runtime, not a raw
// string. The brand erases at runtime (UUID is literally `string`), but the
// type system distinguishes UUID from arbitrary strings. All factory methods
// route through UUID.* helpers so consumers never have to construct the brand
// by hand.
//
// Guid.ToString(format) uses literal-argument dispatch via the WhenArg0StringEquals
// filter to discriminate between the "N" form (strip hyphens) and the parameterless
// identity form. The compiler walks declarations in source order and picks the first
// whose filter matches the call site, so more specific filters must come BEFORE
// the fallback.

using System;
using Metano.Annotations;

// Guid.NewGuid() → UUID.newUuid()
[assembly: MapMethod(
    typeof(Guid),
    nameof(Guid.NewGuid),
    JsTemplate = "UUID.newUuid()",
    RuntimeImports = "UUID"
)]

// Guid.Parse(s) → UUID.create(s) — wraps an existing string as a UUID.
[assembly: MapMethod(
    typeof(Guid),
    nameof(Guid.Parse),
    JsTemplate = "UUID.create($0)",
    RuntimeImports = "UUID"
)]

// Guid.Empty → UUID.empty
[assembly: MapProperty(
    typeof(Guid),
    nameof(Guid.Empty),
    JsTemplate = "UUID.empty",
    RuntimeImports = "UUID"
)]

// Guid.ToString("N") → UUID in compact (no-hyphen) form.
// Must be declared before the unfiltered fallback below.
[assembly: MapMethod(
    typeof(Guid),
    nameof(Guid.ToString),
    WhenArg0StringEquals = "N",
    JsTemplate = "$this.replace(/-/g, \"\")"
)]

// Guid.ToString() / Guid.ToString(other-format) → identity (the value is already
// a branded string at runtime). Acts as the fallback for any call that doesn't
// match a more specific WhenArg0StringEquals filter above.
[assembly: MapMethod(typeof(Guid), nameof(Guid.ToString), JsTemplate = "$this")]
