# Contract: Diagnostics `MS0064`–`MS0066`

**Normative for**: FR-037, FR-038, FR-058, FR-059, FR-061. This spec PR reserves the codes in
[diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md). `MS0063` is the
last allocated code (#440, `AmbiguousRuntimeDispatch`); spec 009's in-flight S6b
(`feat/268-s6b-json-option-diagnostics` at `1b453856`) extends `MS0037` and takes no new code. The next free
code after this block is `MS0067`.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0064` | `Int64EnumValuePrecision` | Warning | the enum member |
| `MS0065` | `BigIntAtInteropBoundary` | Warning | the `[Import]` / `[Emit]` / `[External]` declaration, or the user `[MapMethod]` / `[MapProperty]` attribute (once) |
| `MS0066` | `UnsupportedInt64Format` | Warning | the format or `NumberStyles` argument (the interpolation hole for `$"{x:…}"`) |

All three are warnings: the output is emitted and type-checks; each names a place where the 3.0 behavior
differs from C# or from 2.x interop expectations.

## Reasons

**MS0064 — Int64EnumValuePrecision** (Warning). FR-058.

1. A member of an enum whose underlying type is `long`, `ulong`, `nint` or `nuint` has a constant value that
   `Number(value)` does not represent exactly.
2. A member of a `[Flags]` enum with such an underlying type has a value of 2^31 or more: JS bit operators on
   `number` keep only 32 bits, so `A | B` and `HasFlag` lose it.

Example: `Enum member 'Big.A' has the value 9007199254740993, which TypeScript enum members cannot hold exactly; it is emitted as 9007199254740992 (spec 012 FR-058).`

**MS0065 — BigIntAtInteropBoundary** (Warning). FR-059.

1. A parameter, return, property or field type of an `[Import]`, `[Emit]` or `[External]`
   member or type is `long` / `ulong` / `nint` / `nuint`, or contains one (nullable, array, generic argument,
   delegate parameter or return).
2. A user assembly (not `src/Metano/Runtime`) maps a BCL member whose signature contains a 64-bit integer with
   `[MapMethod]` / `[MapProperty]`; the template's JavaScript receives or must return a `bigint`.

Example: `'Lib.Count()' returns 'long', which is a bigint in TypeScript; the imported JavaScript must return a bigint, not a number (spec 012 FR-059).`

**MS0066 — UnsupportedInt64Format** (Warning). FR-037, FR-038.

1. A constant format string passed to `ToString` on a 64-bit value, or used in an interpolation hole over
   one, is a valid .NET numeric format outside the set `formatInt64` implements (`C`, `P`, `E`, `B`, a
   custom format such as `#,##0`). The call throws `NotSupportedException` at run time.
2. A constant `NumberStyles` passed to `long.Parse` / `TryParse` (or the `ulong` ones) is not one of
   `Integer`, `HexNumber`, `Integer | AllowThousands`, `Number`. The call is emitted with the folded flags
   value and throws `NotSupportedException` at run time.

Example: `Format '#,##0' is not supported for 64-bit integers in TypeScript; the call throws NotSupportedException at run time (spec 012 FR-037).`

An invalid format (`"Q"`) is not reported: .NET throws `FormatException` for it, and so does the runtime.
