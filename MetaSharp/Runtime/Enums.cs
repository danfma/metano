// Declarative BCL → JavaScript mappings for System.Enum members.
//
// Enum.Parse<T>(text) is intentionally NOT mapped here because it requires accessing
// the method's TypeArguments[0].Name to embed the enum name in the lowered expression
// (`T[text as keyof typeof T]`), and the JsTemplate system has no $T placeholder for
// generic type arguments. It stays hardcoded in BclMapper for now.

using System;
using MetaSharp.Annotations;

// flags.HasFlag(other) → (flags & other) === other
// HasFlag is defined on System.Enum and inherited by every concrete enum, so the
// `typeof(Enum)` declaration matches calls on any [Flags]-tagged enum at the call site.
// Roslyn always resolves the call to System.Enum.HasFlag — never to a derived type —
// because the method isn't overridden.
[assembly: MapMethod(typeof(Enum), "HasFlag", JsTemplate = "($this & $0) === $0")]
