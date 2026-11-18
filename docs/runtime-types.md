# Runtime types and interop

How Metano answers runtime questions about values and how C# code reaches untyped
JavaScript data. The normative text is
[spec 011](../specs/011-runtime-collections-and-types/spec.md); this page is the
consumer guide, filled in stage by stage.

## Four-way table

Four spellings move a value between typed C# and untyped JavaScript (spec 011
FR-037). They differ in direction, in what is checked at run time, and in how member
names are spelled after them.

| Spelling | Direction | Runtime check | Result static type | Use |
|---|---|---|---|---|
| `dynamic d` | none (a static type) | none; implicit conversions out of it are unchecked | `dynamic` → `any` | raw JS data, member access bound at run time; **member names verbatim** |
| `x.AsDynamic()` | typed → dynamic | none | `dynamic` | call members JavaScript has but C# does not know; **a Metano member `Total` is `total` here** |
| `x.UnsafeCast<T>()` | unknown / dynamic / erased → typed | none (TypeScript), checked (.NET) | `T` | data of a known shape without identity (parsed JSON) |
| `(T)x`, `x as T`, `x is T` | unknown / dynamic → typed | descriptor-based when answerable, else unchecked + `MS0055` | `T` / `T?` / `bool` | ordinary C# |

`UnsafeCast<T>()` and `AsDynamic()` live in `Metano.Interop.InteropExtensions`:

```csharp
using Metano.Interop;

var dto = raw.UnsafeCast<UserDto>();   // raw as unknown as UserDto
var any = order.AsDynamic();           // (order as any)
```

- **`UnsafeCast<T>()`** is compile-time only: no check, no descriptor, no helper call
  and no diagnostic in TypeScript. On .NET it is `(T)value`, which throws
  `InvalidCastException` for a value of another type where TypeScript succeeds
  silently (OD-15): keep it for values whose shape you know. Dart has no unchecked
  cast, so there `UnsafeCast<T>()` lowers to a checked `as T`, which throws
  (`TypeError`) where TypeScript does not.
- **`AsDynamic()`** returns its argument unchanged. Member names after a `dynamic`
  value are verbatim, so a Metano member `Total`, emitted as `total`, would read
  `undefined` through `x.AsDynamic().Total`. Until dynamic member access lands
  (spec 011 FR-030), a member access on an `AsDynamic()` result reports `MS0001`;
  use `Js.Code` for the access instead.

**Status (3.0.0 stages).** `UnsafeCast<T>()` and `AsDynamic()` are delivered (stage
011f). `dynamic` member access on an `AsDynamic()` result and its `MS0057` warning
arrive in the 011e/011f follow-up that replaces the interim `MS0001` guard. The
last row is the target text: today an explicit cast is checked with `castTo` for a
class target the module can import, and asserted without a check (and without
`MS0055`) for an interface, a generic or an erased target, until stage 011b's runtime
descriptors land. `Js.Code` inline TypeScript is described in
[BCL mappings § Adding your own mappings](bcl-mappings.md#4-jscode--inline-typescript-at-one-use-site).
