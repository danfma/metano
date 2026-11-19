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
  `undefined` through `x.AsDynamic().Total`: `MS0057` (a warning) reports such an
  access when the static type of `x` emits the member under another name (camelCase or
  a `[Name]` rename). An access that names a member the type lacks, or one spelled the
  same, is emitted without a warning: `x.AsDynamic().extra` is `(x as any).extra`.

**Status (3.0.0 stages).** `UnsafeCast<T>()` and `AsDynamic()` are delivered (stage
011f), and so is member access on an `AsDynamic()` result with its `MS0057` warning
(TypeScript; Dart keeps reporting `MS0001` for it until after 3.0.0, D28). The
last row is the target text: today an explicit cast is checked with `castTo` for a
class target the module can import, and asserted without a check (and without
`MS0055`) for an interface, a generic or an erased target, until stage 011b's runtime
descriptors land. `Js.Code` inline TypeScript is described in
[BCL mappings § Adding your own mappings](bcl-mappings.md#4-jscode--inline-typescript-at-one-use-site).

## dynamic

A C# `dynamic` value is TypeScript `any` (spec 011 FR-030, D11). The C# runtime binder (the
DLR) resolves every operation on it at run time; Metano does not emit the binder, so each
operation becomes the plain JavaScript one.

| C# | TypeScript |
|---|---|
| `dynamic d` (field, parameter, return, local) | `d: any` |
| `d.Name`, `d.Name = v`, `d.Run(1, x)`, `d[i]`, `d(3)` | the same, **member names verbatim**: `d.Name`, `d.Run(1, x)` |
| `d.ToString()`, `d.GetHashCode()`, `d.Equals(x)`, `d.GetType()` | as on an `object` receiver: `d.toString()`, `valueHashCode(d)`, … |
| `d.M<int>()` | `d.M()` (type arguments dropped) |
| `d + 1`, `d == x`, `-d`, `d += 1L` | the JavaScript operator, with no `int` wrapping, `bigint`, `Decimal` or `char` lowering (`d += 1L` is `d += 1`) |
| `i += d`, `c += d`, `s += d` on a typed `int` / `char` / `short` | the JavaScript operator, then the target's store conversion, as for any compound assignment to it (`i = (i + d) \| 0`) |
| `string s = d;`, `F(d)` for `F(string)`, `return d;`, `if (d)`, `$"{d}"`, `foreach (var x in d)`, `await d` | nothing: an implicit conversion out of `dynamic` is not checked |
| `(int)d`, `(byte)d`, `(double)d`, `(char)d` | `numberFromDynamic(d, "Int32")`, …, `charFromDynamic(d)` |
| `(long)d`, `(ulong)d`, `(BigInteger)d` | `bigintFromDynamic(d, "Int64")`, … |
| `(decimal)d` | `new Decimal(decimalSourceFromDynamic(d))` |
| `(int?)d` | `d == null ? null : numberFromDynamic(d, "Int32")` |
| `(Circle)d`, `(IShape)d`, `d as Circle`, `d is Circle c`, `d switch { int n => … }` | as the same test or cast of an `object` value |
| `F(d)` with one candidate, `k.Ov(d)` / `new C(d)` whose candidates are exactly one overload group | the bound call, or the group's runtime dispatcher |

The numeric helpers convert from the value the `dynamic` holds, with the helper the compiler
uses for the same cast from that type: an integer `number` in the 32-bit range as an integer
(wrapped, or range-checked in a checked context), any other `number` as a `double`
(`int32FromDouble`, `int64FromDouble`, …), a `bigint` as a 64-bit integer, a `Decimal` as a
`decimal` (`int64FromDecimal`, …: out of range it throws `OverflowException` in every context). `null` throws `InvalidCastException`
for a non-nullable target, and so does a value that is not numeric (`"42"`, `true`).

`MS0053` (error) reports what cannot pass through: an initializer on a construction bound at
run time, a named or `ref` / `out` argument on a call of a `dynamic` receiver, a user-defined
operator or conversion the binder would choose at run time (`money + d`, `(Money)d`,
`Money m = d;`), and a call whose `dynamic` argument leaves several candidates that no single
dispatcher covers (`Console.WriteLine(d)`: write `Console.WriteLine((object)d)`).

The Dart target keeps the form it had before FR-030 until after 3.0.0 (D28): member names
follow its naming policy, casts are `as T`, and no `MS0053` is reported.

Differences from .NET, documented and not diagnosed (OD-11, OD-12):

- Member names are verbatim, so a Metano object read through `dynamic` misses its emitted
  names: `dog.Name` is `undefined`, since the class emits `name`.
- `==` with mixed types (`1 == "1"`, which .NET rejects), `+` with `null`, and integer division
  (`7 / 2` is `3.5`) follow JavaScript.
- A failed conversion throws `InvalidCastException`; .NET throws `RuntimeBinderException`.
- An overload dispatcher chooses on the runtime values of every argument, where the binder uses
  the static types of the arguments that are not `dynamic`.
- An implicit conversion passes the value as is: `F(long x)` called as `F(d)` passes a `number`
  where a `bigint` is expected.
- A `char` held in a `dynamic` is a one-character string: only a `char` target accepts it.
- `d.E += h` on an event passes through as a compound assignment; `d + d` with a user-defined
  operator is not detected; `using (d)` reports `MS0001`, as `using` does for any operand.
- A `dynamic` value mixed with a `long` (`d + someLong`, `l += d`) throws JavaScript's
  `TypeError` for mixing a `bigint` with a `number`.
- A `char` operand of an operator with a `dynamic` operand is its one-character string
  (`c + d` concatenates), where .NET adds the code.
- Until stage 011b, a cast to an interface (`(IShape)d`) is not checked.

## Boxed values

A C# boxing conversion of an enum or `[InlineWrapper]` value (to `object`, `ValueType` or `Enum`) becomes a runtime `Boxed` value, so `GetType()`, `ToString()` and `Equals` answer as in .NET for `object`-typed values (spec 011 FR-050, FR-051). The runtime recognises a box by its string brand field `$boxed === true`, never by `instanceof`, so code that never boxes bundles no box code and a box made by a second copy of `metano-runtime` is still recognised. The limit: any object that carries an own `$boxed: true` field is taken for a box by `unbox`, `isType`, `asType`, `castTo`, `getRuntimeType` and the equality helpers. `Boxed.toString()` follows .NET's enum formatting: the member name, the number for an undeclared value (`"99"`), the comma-separated names for a `[Flags]` combination, and the whole number when a `[Flags]` value has an undeclared bit (`(Perms)9` prints `"9"`).
