// Declarative BCL → JavaScript mappings for the System.* date/time APIs.
//
// DateTime / DateOnly / DateTimeOffset / TimeSpan all lower to the JS Temporal proposal
// (currently the @js-temporal/polyfill); the type-level mapping (DateOnly → PlainDate,
// etc.) is handled by TypeMapper. This file declares the runtime *member* lowerings —
// the static factories and the helper extensions used by the transpiled code at the
// call site.
//
// DateOnly is .NET 6+ and is NOT available in netstandard2.0, which is the target
// framework of this MetaSharp project. The DateOnly.DayNumber mapping therefore stays
// hardcoded in BclMapper.cs for now. Migrating it requires multi-targeting the
// MetaSharp project to include net6.0 (or later) so `typeof(DateOnly)` resolves; that's
// tracked as a follow-up alongside the broader Decimal / runtime decisions.

using System;
using MetaSharp.Annotations;

// DateTimeOffset.UtcNow → Temporal.Now.zonedDateTimeISO()
[assembly: MapProperty(typeof(DateTimeOffset), nameof(DateTimeOffset.UtcNow),
    JsTemplate = "Temporal.Now.zonedDateTimeISO()")]
