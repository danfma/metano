// Declarative BCL → JavaScript mappings for System.Enum members.

using System;
using MetaSharp.Annotations;

// flags.HasFlag(other) → (flags & other) === other
// HasFlag is defined on System.Enum and inherited by every concrete enum, so the
// `typeof(Enum)` declaration matches calls on any [Flags]-tagged enum at the call site.
// Roslyn always resolves the call to System.Enum.HasFlag — never to a derived type —
// because the method isn't overridden.
[assembly: MapMethod(typeof(Enum), nameof(Enum.HasFlag), JsTemplate = "($this & $0) === $0")]

// Enum.Parse<T>(text) → T[text as keyof typeof T]
// Uses the $T0 placeholder to embed the call site's generic method type-argument name
// (the enum type) into the lowered expression. So `Enum.Parse<Status>("Active")` lowers
// to `Status["Active" as keyof typeof Status]`, which TypeScript validates against the
// enum's known member names at compile time.
[assembly: MapMethod(typeof(Enum), nameof(Enum.Parse),
    JsTemplate = "$T0[$0 as keyof typeof $T0]")]
