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

## Production checks: turning them off (spec 011 FR-058)

Metano's collections check every index and detect a collection modified during enumeration, as .NET does. A production app may turn both off. **Turning them off changes semantics**: an out-of-range read returns `undefined` instead of throwing `ArgumentOutOfRangeException`, a write past the end grows the array (with holes), and a modification during enumeration is not detected. It is opt-in per app.

A fully unchecked app needs **two switches**:

1. **The `metano-unchecked` condition**, for packages. `metano-runtime` ships `dist/` (checks on) and `dist-unchecked/` (checks off, no check code at all); the condition selects the second for the runtime and, through the runtime, for every Metano-built library the app depends on.
2. **The `__METANO_CHECKS__: false` define**, for generated code. The `foreach` loops the compiler generates (in the app and in its libraries) read the flag once per loop; the define removes their checks.

Vite (`vite.config.ts`):

```ts
import { defaultClientConditions, defineConfig } from "vite";

export default defineConfig({
  resolve: { conditions: ["metano-unchecked", ...defaultClientConditions] },
  define: { __METANO_CHECKS__: "false" },
});
```

(`resolve.conditions` replaces Vite's defaults, hence the spread; set `ssr.resolve.conditions` the same way for SSR builds.)

esbuild:

```sh
esbuild src/main.ts --bundle --conditions=metano-unchecked --define:__METANO_CHECKS__=false --outfile=dist/main.js
```

bun:

```sh
bun build src/main.ts --conditions metano-unchecked --define __METANO_CHECKS__=false --outdir dist
```

Without a bundler, `node --conditions=metano-unchecked app.js` and `bun --conditions=metano-unchecked app.ts` select the unchecked runtime; the generated loops keep their checks there (no define), which costs one global read per loop. Setting `globalThis.__METANO_CHECKS__` at run time is not a supported switch. The `source` condition resolves the runtime's `.ts` sources, where the define alone turns the checks off.

## Collection benchmark and size budgets (spec 011 stage 011c, NFR-001 / NFR-008)

Recorded by `bun run bench` and `bun run bench:node` in `targets/js/metano-runtime` (FR-039) on 2026-11-19, on an **Apple M1 Pro** (darwin arm64, the reference machine of research R17) with **Node.js 24.11.0** and **Bun 1.4.2**, after the W4-33 dual dist. Shape: hot helper, one pass over 1e4 elements per kernel call, 500 calls per run, 10 warm-up runs, median of 21 timed runs, every (case, dist, role) in a fresh process, bundled by esbuild. Normalized = raw × C_ref / C_measured, with C_ref = 0.59 ns (Node) / 0.47 ns (Bun) and C_measured ≈ 0.53 ns (Node) / 0.54 ns (Bun), the per-process calibration kernel `a[i] = a[i] + 1`. The gate is `dist/` (the default build) on both engines, with per-engine budgets where NFR-001 sets them (W4-27, W4-34). The `Dictionary` / `HashSet` rows are over budget in R1 and owned by hand-in R2 (W4-35).

| Case | Dist | Budget Node / Bun (ns) | Node raw / norm (ns) | Bun raw / norm (ns) | Verdict |
|---|---|---|---|---|---|
| List.get (sequential) | `dist/` (checked) | 1 | 0.25 / 0.27 | 0.28 / 0.26 | ok |
| List.get (sequential) | `dist-unchecked/` + define | 1 | 0.00 / 0.01 | -0.00 / -0.00 | ok |
| List.get (data-driven index) | `dist/` (checked) | 1.75 / 1 | 1.31 / 1.46 | 0.31 / 0.28 | ok |
| List.get (data-driven index) | `dist-unchecked/` + define | 1.75 / 1 | -0.03 / -0.04 | -0.01 / -0.01 | ok |
| List.set | `dist/` (checked) | 1.75 / 1 | 1.62 / 1.79 | 0.48 / 0.44 | **miss** |
| List.set | `dist-unchecked/` + define | 1.75 / 1 | 0.17 / 0.18 | 0.00 / 0.00 | ok |
| list[i] += 1 (direct form) | `dist/` (checked) | 1.5 | 1.45 / 1.56 | 0.64 / 0.58 | **miss** |
| list[i] += 1 (direct form) | `dist-unchecked/` + define | 1.5 | 0.16 / 0.18 | 0.00 / 0.00 | ok |
| list[i] += 1 (closure fallback, update) | `dist/` (checked) | 3 | 1.55 / 1.74 | 2.64 / 2.43 | ok |
| get at a polymorphic site (4 element types) | `dist/` (checked) | 2 | 0.71 / 0.79 | 1.50 / 1.04 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist/` (checked) | 2 | 0.19 / 0.21 | 0.44 / 0.40 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist-unchecked/` + define | 2 | -0.01 / -0.01 | 0.12 / 0.11 | ok |
| List.add | `dist/` (checked) | 0.5 | 0.38 / 0.42 | 0.12 / 0.10 | ok |
| lowered foreach over List (per element; final $c form) | `dist/` (checked) | 1.25 / 0.5 | 0.34 / 0.37 | -0.48 / -0.42 | ok |
| lowered foreach over List (per element; final $c form) | `dist-unchecked/` + define | 1.25 / 0.5 | -0.11 / -0.12 | -0.47 / -0.44 | ok |
| lowered foreach, guard read on every step (superseded R0 form) | `dist/` (checked) | — | 5.39 / 6.13 | 48.79 / 45.28 | reported |
| lowered foreach, guard read on every step (superseded R0 form) | `dist-unchecked/` + define | — | -0.11 / -0.13 | -0.47 / -0.43 | reported |
| List [Symbol.iterator] (for..of, interop) | `dist/` (checked) | — | 1.55 / 1.77 | 2.40 / 2.21 | reported |
| Dictionary.get (number keys) | `dist/` (checked) | 2 | 11.35 / 12.65 | 7.87 / 7.21 | **over budget, R2** |
| Dictionary.set (number keys, overwrite) | `dist/` (checked) | 2 | 4.47 / 5.03 | 5.16 / 4.71 | **over budget, R2** |
| Dictionary.containsKey (string keys) | `dist/` (checked) | 2 | 4.81 / 5.36 | 3.14 / 2.12 | **over budget, R2** |
| Dictionary.add (string keys, fresh dictionary) | `dist/` (checked) | 2 | 20.66 / 23.35 | 21.81 / 15.16 | **over budget, R2** |
| HashSet.add (numbers, fresh set) | `dist/` (checked) | 2 | 9.36 / 10.40 | 6.89 / 6.21 | **over budget, R2** |
| HashSet.contains (numbers) | `dist/` (checked) | 2 | 2.67 / 3.02 | 3.22 / 2.92 | **over budget, R2** |
| HashSet.remove + add (numbers) | `dist/` (checked) | 2 | 8.80 / 9.75 | 5.28 / 4.75 | **over budget, R2** |
| Queue.enqueue + dequeue | `dist/` (checked) | 1.5 | 1.77 / 1.95 | 1.16 / 1.03 | **miss** |
| Stack.push + pop | `dist/` (checked) | 1.5 | 0.71 / 0.77 | -0.05 / -0.05 | ok |
| Dictionary.get (record keys, reported) | `dist/` (checked) | — | 70.75 / 79.05 | 47.25 / 32.14 | reported |

Three rows miss on Node.js in this run, by little: `List.set` (1.79 against 1.75 ns), `list[i] += 1` (1.56 against 1.5 ns) and `Queue.enqueue + dequeue` (1.95 against 1.5 ns; Bun 1.03). Across the three clean runs of the day, `List.set` measured 1.74–1.79 ns and `list[i] += 1` 1.49–1.56 ns, so both sit on their budget. The remaining cost is the bounds compare and the `_version` store that FR-057 requires; it is not the checks mechanism (`dist-unchecked/` measures about 0.2 ns).

Sizes (FR-043, minified + gzipped, from `dist/`), against NFR-008 (the collection rows raised to the measured size + 10% by W4-25 (d): FR-016's exception classes, about 0.97 KB, plus `elementEquals` / `defaultEquals`, about 0.5 KB, shared when several collections are used; slimming the exceptions is a follow-up):

| Entry | Budget | bun | esbuild |
|---|---|---|---|
| `List` alone (full surface) | 4586 B | 4099 B | 4057 B |
| `Dictionary` alone (with the views) | 4506 B | 4052 B | 3995 B |
| `HashSet` alone | 4283 B | 3859 B | 3810 B |
| `Queue` alone | 3128 B | 2794 B | 2745 B |
| `Stack` alone | 2947 B | 2634 B | 2578 B |
| `List` + `Dictionary` + `HashSet` together | 7112 B | 6401 B | 6302 B |
| `isType` + `getRuntimeType` + one class descriptor (011b row) | 1792 B | 1792 B | 1761 B |
