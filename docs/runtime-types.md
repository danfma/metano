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

Recorded by `bun run bench` and `bun run bench:node` in `targets/js/metano-runtime` (FR-039) on 2026-11-19, on an **Apple M1 Pro** (darwin arm64, the reference machine of research R17) with **Node.js 24.11.0** and **Bun 1.4.2**. Shape: hot helper, one pass over 1e4 elements per kernel call, 500 calls per run, 10 warm-up runs, median of 21 timed runs, every (case, mode, role) in a fresh process. Normalized = raw × C_ref / C_measured, with C_ref = 0.59 ns (Node) / 0.47 ns (Bun) and C_measured ≈ 0.57 ns (Node) / 0.56 ns (Bun), the per-process calibration kernel `a[i] = a[i] + 1`. The gate is the default build on both engines; the larger normalized figure counts.

**Status: NFR-001 is not met on the default build.** The FR-058 guard `(typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)` reads an undeclared global on every guarded call; with the constant absent this lookup costs about 70–150 ns per call on Bun and about 5–10 ns on Node, far above every budget. With the constant defined `false` (the guarded code removed by the bundler) every row passes; with it set on `globalThis` at run time Bun passes and Node misses `set` / read-modify-write by a small margin (a global property load per call). The lowered `foreach` with the guard hoisted to a function-local `const $c` costs 0.00 ns (Bun) / 1.04 ns (Node) per element on the default build. Stage 011c is blocked on this row set until the design is revisited or the maintainer records a decision (NFR-001's rule).

| Case | Mode | Budget (ns) | Node raw / norm (ns) | Bun raw / norm (ns) | Verdict (larger) |
|---|---|---|---|---|---|
| List.get (sequential) | default (checks on) | 1 | 5.38 / 5.76 | 81.08 / 67.78 | **miss** |
| List.get (sequential) | `define` false | 1 | 0.01 / 0.01 | -0.01 / -0.01 | ok |
| List.get (sequential) | set on `globalThis` | 1 | 0.25 / 0.27 | 0.30 / 0.25 | ok |
| List.get (data-driven index) | default (checks on) | 1 | 6.25 / 6.64 | 73.49 / 58.99 | **miss** |
| List.get (data-driven index) | `define` false | 1 | -0.08 / -0.08 | 0.00 / 0.00 | ok |
| List.get (data-driven index) | set on `globalThis` | 1 | 1.34 / 1.42 | 0.34 / 0.29 | **miss** |
| List.set | default (checks on) | 1 | 9.40 / 9.96 | 150.78 / 123.53 | **miss** |
| List.set | `define` false | 1 | 0.13 / 0.14 | 0.01 / 0.00 | ok |
| List.set | set on `globalThis` | 1 | 1.74 / 1.82 | 0.52 / 0.44 | **miss** |
| list[i] += 1 (direct form) | default (checks on) | 1.5 | 14.50 / 15.39 | 270.28 / 235.70 | **miss** |
| list[i] += 1 (direct form) | `define` false | 1.5 | 0.17 / 0.18 | -0.00 / -0.00 | ok |
| list[i] += 1 (direct form) | set on `globalThis` | 1.5 | 1.47 / 1.58 | 0.66 / 0.58 | **miss** |
| list[i] += 1 (closure fallback, update) | default (checks on) | 3 | 15.81 / 16.76 | 361.97 / 316.62 | **miss** |
| get at a polymorphic site (4 element types) | default (checks on) | 2 | 5.66 / 6.01 | 87.43 / 58.53 | **miss** |
| get at a site seeing List and a fixed-size adopted list | default (checks on) | 2 | 5.41 / 5.84 | 99.37 / 83.64 | **miss** |
| get at a site seeing List and a fixed-size adopted list | `define` false | 2 | -0.01 / -0.01 | -0.00 / -0.00 | ok |
| get at a site seeing List and a fixed-size adopted list | set on `globalThis` | 2 | 0.20 / 0.21 | 0.46 / 0.39 | ok |
| List.add | default (checks on) | 0.5 | 5.06 / 5.35 | 60.86 / 53.31 | **miss** |
| lowered foreach over List (per element) | default (checks on) | 0.5 | 5.65 / 6.04 | 51.93 / 45.49 | **miss** |
| lowered foreach over List (per element) | `define` false | 0.5 | -0.12 / -0.13 | -0.50 / -0.43 | ok |
| lowered foreach over List (per element) | set on `globalThis` | 0.5 | -0.08 / -0.09 | -0.50 / -0.43 | ok |
| lowered foreach, guard hoisted to a local const $c | default (checks on) | — | 0.96 / 1.04 | 0.00 / 0.00 | reported |
| lowered foreach, guard hoisted to a local const $c | `define` false | — | -0.10 / -0.11 | -0.49 / -0.43 | reported |
| lowered foreach, guard hoisted to a local const $c | set on `globalThis` | — | -0.09 / -0.09 | -0.50 / -0.43 | reported |
| List [Symbol.iterator] (for..of, interop) | default (checks on) | — | 8.61 / 9.31 | 57.60 / 49.36 | reported |
| Dictionary.get (number keys) | default (checks on) | 2 | 10.56 / 11.30 | 8.89 / 7.74 | **miss** |
| Dictionary.set (number keys, overwrite) | default (checks on) | 2 | 4.40 / 4.72 | 5.98 / 5.20 | **miss** |
| Dictionary.containsKey (string keys) | default (checks on) | 2 | 4.73 / 5.09 | 3.31 / 2.25 | **miss** |
| Dictionary.add (string keys, fresh dictionary) | default (checks on) | 2 | 26.15 / 27.61 | 78.41 / 52.46 | **miss** |
| HashSet.add (numbers, fresh set) | default (checks on) | 2 | 14.40 / 15.29 | 65.21 / 60.05 | **miss** |
| HashSet.contains (numbers) | default (checks on) | 2 | 1.05 / 0.64 | 3.08 / 2.84 | **miss** |
| HashSet.remove + add (numbers) | default (checks on) | 2 | 13.94 / 11.92 | 39.52 / 36.62 | **miss** |
| Queue.enqueue + dequeue | default (checks on) | 1.5 | 15.36 / 16.32 | 67.49 / 58.86 | **miss** |
| Stack.push + pop | default (checks on) | 1.5 | 7.14 / 7.66 | 118.91 / 103.42 | **miss** |
| Dictionary.get (record keys, reported) | default (checks on) | — | 106.43 / 112.24 | 138.47 / 92.65 | reported |

Sizes (FR-043, minified + gzipped, bun / esbuild), against NFR-008:

| Entry | Budget | bun | esbuild | Verdict |
|---|---|---|---|---|
| `List` alone (full surface) | 2560 B | 3940 B | 3884 B | **miss** |
| `Dictionary` alone (with the views) | 3072 B | 3868 B | 3805 B | **miss** |
| `HashSet` alone | 1638 B | 3659 B | 3618 B | **miss** |
| `Queue` alone | 1228 B | 2608 B | 2553 B | **miss** |
| `Stack` alone | 1228 B | 2441 B | 2382 B | **miss** |
| `isType + getRuntimeType + one class descriptor` (011b row) | 1792 B | 2445 B | 2419 B | **miss** (was 1758 / 1727 B: `getRuntimeType` now closes collection descriptors through `specializeCollection`, which brings `isAssignableFrom`) |

The FR-013 exception classes each collection throws are most of the gap: the four or five exception classes a collection needs gzip to about 0.97 KB together, `defaultEquals` to about 0.5 KB.
