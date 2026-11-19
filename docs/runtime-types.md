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

## Collection benchmark and size budgets (spec 011 stage 011c, NFR-001 / NFR-008)

Recorded by `bun run bench` and `bun run bench:node` in `targets/js/metano-runtime` (FR-039) on 2026-11-19, on an **Apple M1 Pro** (darwin arm64, the reference machine of research R17) with **Node.js 24.11.0** and **Bun 1.4.2**. Shape: hot helper, one pass over 1e4 elements per kernel call, 500 calls per run, 10 warm-up runs, median of 21 timed runs, every (case, mode, role) in a fresh process. Normalized = raw × C_ref / C_measured, with C_ref = 0.59 ns (Node) / 0.47 ns (Bun) and C_measured ≈ 0.53 ns (Node) / 0.54 ns (Bun), the per-process calibration kernel `a[i] = a[i] + 1`. The gate is the default build on both engines (per-engine budget for the lowered `foreach`, W4-27; otherwise the larger normalized figure counts).

**Status.** The lowered `foreach` (final shape: the flag read once into the function-local `$c`, the block's first declaration) is within budget in every mode on both engines. The runtime members (`get`, `set`, the version stores, every class's guarded paths) still miss on the default build, **pending the checks-mechanism decision** (W4-27): their inline guard `(typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)` reads an undeclared global on every call, about 5–10 ns on Node and 50–160 ns on Bun. With the constant defined `false` every row passes. With it set on `globalThis` before the runtime loads, Bun passes and Node misses `set` and the read-modify-write rows (a global property load per call). A module-scope `const` read once was tried and rejected (R1 report): bun and esbuild do not substitute it into its uses in a module that imports anything, and Rollup keeps its global read under a side-effect-only import.

**Setting the flag.** `__METANO_CHECKS__` is a bundle-time constant: a bundler `define` (`__METANO_CHECKS__: "false"`) is the supported way to drop the checks (FR-058). Setting `globalThis.__METANO_CHECKS__` at run time is read on every guarded call today; it is not a supported switch.

| Case | Mode | Budget Node / Bun (ns) | Node raw / norm (ns) | Bun raw / norm (ns) | Verdict |
|---|---|---|---|---|---|
| List.get (sequential) | default (checks on) | 1 | 5.21 / 5.80 | 60.38 / 52.85 | **miss** (pending the checks-mechanism decision) |
| List.get (sequential) | `define` false | 1 | 0.02 / 0.02 | 0.00 / 0.00 | ok |
| List.get (sequential) | set on `globalThis` | 1 | 0.24 / 0.26 | 0.30 / 0.26 | ok |
| List.get (data-driven index) | default (checks on) | 1 | 6.03 / 6.69 | 60.07 / 52.98 | **miss** (pending the checks-mechanism decision) |
| List.get (data-driven index) | `define` false | 1 | -0.04 / -0.04 | -0.00 / -0.00 | ok |
| List.get (data-driven index) | set on `globalThis` | 1 | 1.32 / 1.47 | 0.32 / 0.29 | **miss** |
| List.set | default (checks on) | 1 | 9.07 / 10.05 | 120.80 / 108.60 | **miss** (pending the checks-mechanism decision) |
| List.set | `define` false | 1 | 0.17 / 0.18 | -0.00 / -0.00 | ok |
| List.set | set on `globalThis` | 1 | 1.52 / 1.67 | 0.50 / 0.44 | **miss** |
| list[i] += 1 (direct form) | default (checks on) | 1.5 | 14.00 / 15.60 | 180.22 / 157.91 | **miss** (pending the checks-mechanism decision) |
| list[i] += 1 (direct form) | `define` false | 1.5 | 0.17 / 0.19 | -0.01 / -0.01 | ok |
| list[i] += 1 (direct form) | set on `globalThis` | 1.5 | 1.37 / 1.54 | 0.62 / 0.55 | **miss** |
| list[i] += 1 (closure fallback, update) | default (checks on) | 3 | 15.28 / 17.44 | 178.87 / 163.50 | **miss** (pending the checks-mechanism decision) |
| get at a polymorphic site (4 element types) | default (checks on) | 2 | 5.43 / 6.03 | 57.90 / 40.96 | **miss** (pending the checks-mechanism decision) |
| get at a site seeing List and a fixed-size adopted list | default (checks on) | 2 | 5.23 / 5.79 | 60.00 / 54.58 | **miss** (pending the checks-mechanism decision) |
| get at a site seeing List and a fixed-size adopted list | `define` false | 2 | -0.02 / -0.02 | 0.00 / 0.00 | ok |
| get at a site seeing List and a fixed-size adopted list | set on `globalThis` | 2 | 0.19 / 0.22 | 0.43 / 0.40 | ok |
| List.add | default (checks on) | 0.5 | 4.86 / 5.41 | 57.07 / 52.75 | **miss** (pending the checks-mechanism decision) |
| lowered foreach over List (per element; final $c form) | default (checks on) | 1.25 / 0.5 | 0.31 / 0.34 | -0.46 / -0.43 | ok |
| lowered foreach over List (per element; final $c form) | `define` false | 1.25 / 0.5 | -0.10 / -0.11 | -0.47 / -0.43 | ok |
| lowered foreach over List (per element; final $c form) | set on `globalThis` | 1.25 / 0.5 | -0.09 / -0.10 | -0.47 / -0.43 | ok |
| lowered foreach, guard read on every step (superseded R0 form) | default (checks on) | — | 5.42 / 6.09 | 50.54 / 46.81 | reported |
| lowered foreach, guard read on every step (superseded R0 form) | `define` false | — | -0.11 / -0.12 | -0.49 / -0.43 | reported |
| lowered foreach, guard read on every step (superseded R0 form) | set on `globalThis` | — | -0.08 / -0.09 | -0.49 / -0.43 | reported |
| List [Symbol.iterator] (for..of, interop) | default (checks on) | — | 8.35 / 9.30 | 54.58 / 48.26 | reported |
| Dictionary.get (number keys) | default (checks on) | 2 | 10.25 / 11.32 | 8.61 / 7.61 | **miss** (pending the checks-mechanism decision) |
| Dictionary.set (number keys, overwrite) | default (checks on) | 2 | 4.44 / 4.97 | 5.02 / 4.35 | **miss** (pending the checks-mechanism decision) |
| Dictionary.containsKey (string keys) | default (checks on) | 2 | 5.14 / 5.64 | 2.88 / 1.93 | **miss** (pending the checks-mechanism decision) |
| Dictionary.add (string keys, fresh dictionary) | default (checks on) | 2 | 24.72 / 27.70 | 73.74 / 51.44 | **miss** (pending the checks-mechanism decision) |
| HashSet.add (numbers, fresh set) | default (checks on) | 2 | 12.55 / 14.09 | 64.70 / 59.48 | **miss** (pending the checks-mechanism decision) |
| HashSet.contains (numbers) | default (checks on) | 2 | 2.61 / 2.91 | 3.53 / 3.11 | **miss** (pending the checks-mechanism decision) |
| HashSet.remove + add (numbers) | default (checks on) | 2 | 10.72 / 12.07 | 35.35 / 31.93 | **miss** (pending the checks-mechanism decision) |
| Queue.enqueue + dequeue | default (checks on) | 1.5 | 10.58 / 11.81 | 63.59 / 58.25 | **miss** (pending the checks-mechanism decision) |
| Stack.push + pop | default (checks on) | 1.5 | 4.86 / 5.39 | 56.92 / 51.90 | **miss** (pending the checks-mechanism decision) |
| Dictionary.get (record keys, reported) | default (checks on) | — | 71.53 / 80.00 | 45.34 / 31.24 | reported |

Sizes (FR-043, minified + gzipped, bun / esbuild), against NFR-008 (the five collection rows and the combined row raised to the measured size + 10% by the maintainer, W4-25 (d): FR-016's exception classes, about 0.97 KB, plus `elementEquals` / `defaultEquals`, about 0.5 KB, shared when several collections are used; slimming the exceptions is a follow-up):

| Entry | Budget | bun | esbuild | Verdict |
|---|---|---|---|---|
| `List` alone (full surface) | 4586 B | 4169 B | 4113 B | ok |
| `Dictionary` alone (with the views) | 4506 B | 4096 B | 4030 B | ok |
| `HashSet` alone | 4283 B | 3893 B | 3849 B | ok |
| `Queue` alone | 3128 B | 2843 B | 2786 B | ok |
| `Stack` alone | 2947 B | 2679 B | 2620 B | ok |
| `List` + `Dictionary` + `HashSet` together | 7112 B | 6465 B | 6357 B | ok |
| `isType + getRuntimeType + one class descriptor` (011b row) | 1792 B | 1792 B | 1761 B | ok (W4-24 (f): `getRuntimeType` closes a collection through the class literal's `closeFor`, so it carries no closing code when no collection is in the program) |
