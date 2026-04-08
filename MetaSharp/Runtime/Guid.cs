// Declarative BCL → JavaScript mappings for System.Guid.
//
// In the JS runtime, GUIDs are represented as plain strings (string is the underlying
// JS type for the transpiled Guid family). Most operations are inert because the value
// is already a string at runtime.
//
// Guid.ToString(format) uses literal-argument dispatch via the WhenArg0StringEquals
// filter to discriminate between the "N" form (strip hyphens) and the parameterless
// identity form. The compiler walks declarations in source order and picks the first
// whose filter matches the call site, so the more specific filter must come BEFORE the
// fallback declaration.

using System;
using MetaSharp.Annotations;

// Guid.NewGuid() → crypto.randomUUID()
[assembly: MapMethod(typeof(Guid), nameof(Guid.NewGuid), JsTemplate = "crypto.randomUUID()")]

// Guid.ToString("N") → strip hyphens via String.replace
// Must be declared before the unfiltered fallback below.
[assembly: MapMethod(typeof(Guid), nameof(Guid.ToString),
    WhenArg0StringEquals = "N",
    JsTemplate = "$this.replace(/-/g, \"\")")]

// Guid.ToString() / Guid.ToString(other-format) → identity (the value is already a
// string at runtime). Acts as the fallback for any call that doesn't match a more
// specific WhenArg0StringEquals filter above.
[assembly: MapMethod(typeof(Guid), nameof(Guid.ToString), JsTemplate = "$this")]
