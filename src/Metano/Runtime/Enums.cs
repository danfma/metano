// Declarative BCL → JavaScript mappings for System.Enum members.

using Metano.Annotations;

// flags.HasFlag(other) → (flags & other) === other
// HasFlag is defined on System.Enum and inherited by every concrete enum, so the
// `typeof(Enum)` declaration matches calls on any [Flags]-tagged enum at the call site.
// Roslyn always resolves the call to System.Enum.HasFlag — never to a derived type —
// because the method isn't overridden.
[assembly: MapMethod(typeof(Enum), nameof(Enum.HasFlag), JsTemplate = "($this & $0) === $0")]

// Enum.Parse / Enum.ToObject / Enum.IsDefined read the enum's runtime type descriptor
// (spec 011 FR-024): `enumFromRaw` accepts a declared name, a comma-separated [Flags]
// combination and a numeric string as .NET does, and throws ArgumentException for an
// unknown name; `ToObject` keeps an undeclared integral value, as .NET does. The generic
// forms read the type argument's descriptor through `$R0`, which also makes the frontend
// ask for it, as `typeof(E)` does.
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.Parse),
    WhenParameterTypes = [typeof(string)],
    JsTemplate = "enumFromRaw($R0, $0)",
    RuntimeImports = "enumFromRaw"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.Parse),
    WhenParameterTypes = [typeof(string), typeof(bool)],
    JsTemplate = "enumFromRaw($R0, $0, $1)",
    RuntimeImports = "enumFromRaw"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.Parse),
    WhenParameterTypes = [typeof(Type), typeof(string)],
    JsTemplate = "enumFromRaw($0, $1)",
    RuntimeImports = "enumFromRaw"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.Parse),
    WhenParameterTypes = [typeof(Type), typeof(string), typeof(bool)],
    JsTemplate = "enumFromRaw($0, $1, $2)",
    RuntimeImports = "enumFromRaw"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.ToObject),
    WhenArgCount = 2,
    JsTemplate = "enumFromRaw($0, $1)",
    RuntimeImports = "enumFromRaw"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.IsDefined),
    WhenArgCount = 1,
    JsTemplate = "enumIsDefined($R0, $0)",
    RuntimeImports = "enumIsDefined"
)]
[assembly: MapMethod(
    typeof(Enum),
    nameof(Enum.IsDefined),
    WhenArgCount = 2,
    JsTemplate = "enumIsDefined($0, $1)",
    RuntimeImports = "enumIsDefined"
)]
