// Declarative BCL → JavaScript mappings for System.Guid.
//
// In the JS runtime, GUIDs are represented as plain strings (string is the underlying
// JS type for the transpiled Guid family). Most operations are inert because the value
// is already a string at runtime.
//
// Guid.ToString(format) is intentionally NOT mapped here because the lowering depends
// on the literal value of the format argument ("N" → strip hyphens, default → identity).
// That kind of literal-aware dispatch can't be expressed by the current JsTemplate system
// and stays hardcoded in BclMapper for now. Tracked as a follow-up.

using System;
using MetaSharp.Annotations;

// Guid.NewGuid() → crypto.randomUUID()
[assembly: MapMethod(typeof(Guid), nameof(Guid.NewGuid), JsTemplate = "crypto.randomUUID()")]
