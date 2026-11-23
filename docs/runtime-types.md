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

Recorded by `bun run bench` and `bun run bench:node` in `targets/js/metano-runtime` (FR-039) on 2026-11-19, on an **Apple M1 Pro** (darwin arm64, the reference machine of research R17) with **Node.js 24.11.0** and **Bun 1.4.2**, after the W4-33 dual dist. Shape: hot helper, one pass over 1e4 elements per kernel call, 500 calls per run, 10 warm-up runs, median of 21 timed runs, every (case, dist, role) in a fresh process, bundled by esbuild. Normalized = raw × C_ref / C_measured, with C_ref = 0.59 ns (Node) / 0.47 ns (Bun) and C_measured ≈ 0.59 ns (Node) / 0.56 ns (Bun), the per-process calibration kernel `a[i] = a[i] + 1`. The gate is `dist/` (the default build) on both engines, with per-engine budgets where NFR-001 sets them (W4-27, W4-34). The `Dictionary` / `HashSet` rows are over budget in R1 and owned by hand-in R2 (W4-35).

| Case | Dist | Budget Node / Bun (ns) | Node raw / norm (ns) | Bun raw / norm (ns) | Verdict |
|---|---|---|---|---|---|
| List.get (sequential) | `dist/` (checked) | 1 | 0.24 / 0.26 | 0.29 / 0.26 | ok |
| List.get (sequential) | `dist-unchecked/` + define | 1 | 0.00 / 0.00 | -0.01 / -0.01 | ok |
| List.get (data-driven index) | `dist/` (checked) | 1.75 / 1 | 1.34 / 1.44 | 0.33 / 0.28 | ok |
| List.get (data-driven index) | `dist-unchecked/` + define | 1.75 / 1 | -0.04 / -0.04 | -0.03 / -0.03 | ok |
| List.set | `dist/` (checked) | 1.85 / 1 | 1.69 / 1.44 | 0.48 / 0.44 | ok |
| List.set | `dist-unchecked/` + define | 1.85 / 1 | 0.29 / 0.25 | 0.01 / 0.00 | ok |
| list[i] += 1 (direct form) | `dist/` (checked) | 1.6 / 1.5 | 1.16 / 1.21 | 0.63 / 0.57 | ok |
| list[i] += 1 (direct form) | `dist-unchecked/` + define | 1.6 / 1.5 | 0.17 / 0.18 | 0.00 / 0.00 | ok |
| list[i] += 1 (closure fallback, update) | `dist/` (checked) | 3 | 1.71 / 1.46 | 2.63 / 2.41 | ok |
| get at a polymorphic site (4 element types) | `dist/` (checked) | 2 | 0.80 / 0.69 | 0.07 / 0.05 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist/` (checked) | 2 | 0.20 / 0.21 | 0.29 / 0.26 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist-unchecked/` + define | 2 | -0.01 / -0.01 | 0.12 / 0.11 | ok |
| List.add | `dist/` (checked) | 0.5 | 0.21 / 0.18 | 0.08 / 0.07 | ok |
| lowered foreach over List (per element; final $c form) | `dist/` (checked) | 1.25 / 0.5 | 0.33 / 0.36 | -0.47 / -0.42 | ok |
| lowered foreach over List (per element; final $c form) | `dist-unchecked/` + define | 1.25 / 0.5 | -0.11 / -0.12 | -0.47 / -0.43 | ok |
| lowered foreach, guard read on every step (superseded R0 form) | `dist/` (checked) | — | 6.88 / 7.41 | 51.22 / 46.84 | reported |
| lowered foreach, guard read on every step (superseded R0 form) | `dist-unchecked/` + define | — | -0.12 / -0.11 | -0.50 / -0.43 | reported |
| List [Symbol.iterator] (for..of, interop) | `dist/` (checked) | — | 1.63 / 1.73 | 2.81 / 2.40 | reported |
| Dictionary.get (number keys) | `dist/` (checked) | 2 | 15.39 / 13.25 | 7.87 / 6.86 | **over budget, R2** |
| Dictionary.set (number keys, overwrite) | `dist/` (checked) | 2 | -0.12 / -0.10 | 5.32 / 4.70 | **over budget, R2** |
| Dictionary.containsKey (string keys) | `dist/` (checked) | 2 | 5.17 / 5.60 | 3.01 / 1.79 | **over budget, R2** |
| Dictionary.add (string keys, fresh dictionary) | `dist/` (checked) | 2 | 21.00 / 22.76 | 19.95 / 13.56 | **over budget, R2** |
| HashSet.add (numbers, fresh set) | `dist/` (checked) | 2 | 8.03 / 8.88 | 6.79 / 5.99 | **over budget, R2** |
| HashSet.contains (numbers) | `dist/` (checked) | 2 | 2.63 / 2.90 | 3.34 / 3.02 | **over budget, R2** |
| HashSet.remove + add (numbers) | `dist/` (checked) | 2 | 8.41 / 9.26 | 8.73 / 7.81 | **over budget, R2** |
| Queue.enqueue + dequeue | `dist/` (checked) | 1.5 | 1.98 / 2.22 | 3.10 / 2.22 | **over budget, R2** |
| Queue BFS shape (dequeue 1, enqueue 2; reported) | `dist/` (checked) | — | 3.20 / 3.50 | 8.13 / 7.11 | reported |
| Stack.push + pop | `dist/` (checked) | 1.5 | 0.89 / 0.98 | 0.53 / 0.46 | ok |
| Dictionary.get (record keys, reported) | `dist/` (checked) | — | 72.62 / 80.05 | 69.82 / 46.82 | reported |

`List.set` and `list[i] += 1` carry per-engine budgets on Node.js (1.85 and 1.6 ns, W4-37): across the clean runs of the day they measured 1.74–1.79 and 1.49–1.56 ns, run-to-run noise around the bounds compare and the `_version` store FR-057 requires (`dist-unchecked/` measures about 0.2 ns). The `Queue` row is over budget and owned by hand-in R2 (W4-37); the BFS-shaped row (dequeue one, enqueue two) is reported to show the queue stays amortized O(1) when the two interleave.

**Hand-in R2 (2026-11-19): single-lookup hash core, array-backed queue.** The rows the table above marks R2, measured again after `hash-core` looks a key up once and `Queue` became an array with a head index. The machine was shared with other jobs (load average 7–17), so each row ran twice and the range is shown (normalized ns; budget 2.0, `Queue` 1.5):

| Row | Node.js 24.11.0 | Bun 1.4.2 |
|---|---|---|
| `Dictionary.get` (number keys) | 0.80–1.95 | −0.16–0.19 |
| `Dictionary.set` (number keys, overwrite) | 0.54–0.87 | 0.92–1.04 |
| `Dictionary.containsKey` (string keys) | **2.92–3.31** | 0.32–0.34 |
| `Dictionary.add` (string keys, fresh) | **0.71–3.56** | 1.08–1.70 |
| `HashSet.add` (numbers, fresh set) | **6.25–9.39** | **2.18–3.17** |
| `HashSet.contains` (numbers) | −2.45–−0.45 | −0.11–0.17 |
| `HashSet.remove + add` (numbers) | **1.96–2.84** | 0.74–1.52 |
| `Queue.enqueue + dequeue` | **2.06** (a third run, disturbed, 2.96) | 1.19–1.49 |
| `Queue` BFS shape (reported) | 1.10 | 1.15–1.38 |

Bold rows are still over budget: `HashSet.add` into a fresh set (a `Map` keyed store where the baseline is a native `Set`), `Dictionary.containsKey` with string keys on Node.js, the `Queue` row on Node.js, and, at the top of their noisy ranges, `Dictionary.add` and `HashSet.remove + add` on Node.js. They are **provisional misses** (maintainer decision W4-49): the budgets stay unchanged, and the final numbers come from a quiet-machine re-measure at stage 011g.

**Final measurement and budgets (stage 011g, 2026-11-23, #720).** `bun run bench` and `bun run bench:node` on an **Apple M1 Pro** (darwin arm64) with **Bun 1.4.2** and **Node.js 24.11.0**, default build, same shape as above (1e4 elements x 500 calls, 10 warm-ups, median of 21). Calibration: C_ref = 0.47 ns (Bun) / 0.59 ns (Node.js); C_measured per row 0.51–0.68 ns (Bun) and 0.52–0.55 ns (Node.js). **Load average 2.0–3.0 during the run**, above the "< 2" that `bench/README.md` asks for, so the figures carry some noise; the run was not repeated on a quieter machine.

Per-engine budgets (maintainer decision, 2026-11-23): every budget that already held keeps its value; the four Node.js rows that missed (labelled `011g`) are set to the measured normalized value + 10%, rounded up to two decimals: `Dictionary.containsKey` (string keys) 3.05 -> 3.36 ns, `HashSet.add` 3.42 -> 3.77 ns, `HashSet.remove + add` 2.42 -> 2.67 ns, `Queue.enqueue + dequeue` 2.06 -> 2.27 ns. The Bun budgets are unchanged. The `011g` labels and the provisional status are gone, and `bun run bench --check` and `bun run bench:node --check` gate on these numbers.

| Case | Dist | Budget Node / Bun (ns) | Node raw / norm (ns) | Bun raw / norm (ns) | Verdict |
|---|---|---|---|---|---|
| List.get (sequential) | `dist/` (checked) | 1 | 0.23 / 0.26 | 0.29 / 0.26 | ok |
| List.get (sequential) | `dist-unchecked/` + define | 1 | 0.00 / 0.00 | 0.00 / 0.00 | ok |
| List.get (data-driven index) | `dist/` (checked) | 1.75 / 1 | 1.29 / 1.44 | 0.34 / 0.31 | ok |
| List.get (data-driven index) | `dist-unchecked/` + define | 1.75 / 1 | -0.03 / -0.03 | -0.03 / -0.02 | ok |
| List.set | `dist/` (checked) | 1.85 / 1 | 1.52 / 1.73 | 0.46 / 0.42 | ok |
| List.set | `dist-unchecked/` + define | 1.85 / 1 | 0.14 / 0.16 | 0.00 / 0.00 | ok |
| list[i] += 1 (direct form) | `dist/` (checked) | 1.6 / 1.5 | 1.31 / 1.47 | 0.63 / 0.58 | ok |
| list[i] += 1 (direct form) | `dist-unchecked/` + define | 1.6 / 1.5 | 0.16 / 0.18 | 0.00 / 0.00 | ok |
| list[i] += 1 (closure fallback, update) | `dist/` (checked) | 3 | 1.53 / 1.70 | 2.67 / 2.41 | ok |
| get at a polymorphic site (4 element types) | `dist/` (checked) | 2 | 0.26 / 0.29 | 0.07 / 0.05 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist/` (checked) | 2 | 0.19 / 0.21 | 0.44 / 0.40 | ok |
| get at a site seeing List and a fixed-size adopted list | `dist-unchecked/` + define | 2 | -0.01 / -0.01 | 0.12 / 0.11 | ok |
| List.add | `dist/` (checked) | 0.5 | 0.34 / 0.38 | 0.08 / 0.07 | ok |
| lowered foreach over List (per element; final $c form) | `dist/` (checked) | 1.25 / 0.5 | 0.31 / 0.35 | -0.46 / -0.43 | ok |
| lowered foreach over List (per element; final $c form) | `dist-unchecked/` + define | 1.25 / 0.5 | -0.10 / -0.12 | -0.46 / -0.43 | ok |
| lowered foreach, guard read on every step (superseded R0 form) | `dist/` (checked) | — | 5.30 / 5.80 | 48.43 / 44.83 | reported |
| lowered foreach, guard read on every step (superseded R0 form) | `dist-unchecked/` + define | — | -0.10 / -0.11 | -0.44 / -0.40 | reported |
| List [Symbol.iterator] (for..of, interop) | `dist/` (checked) | — | 1.57 / 1.77 | 2.39 / 2.08 | reported |
| Dictionary.get (number keys) | `dist/` (checked) | 2 | 1.49 / 1.69 | -0.01 / -0.01 | ok |
| Dictionary.set (number keys, overwrite) | `dist/` (checked) | 2 | 1.42 / 1.62 | 1.11 / 1.02 | ok |
| Dictionary.containsKey (string keys) | `dist/` (checked) | 3.36 / 2 | 2.76 / 3.05 | 0.38 / 0.27 | ok |
| Dictionary.add (string keys, fresh dictionary) | `dist/` (checked) | 2 | 1.16 / 1.30 | 1.35 / 0.94 | ok |
| HashSet.add (numbers, fresh set) | `dist/` (checked) | 3.77 / 2 | 3.03 / 3.42 | 1.45 / 1.34 | ok |
| HashSet.add (numbers, fresh set; 3.0 class vs the 2.x runtime HashSet, reported) | `dist/` (checked) | — | 4.95 / 5.53 | -1.82 / -1.62 | reported |
| HashSet.contains (numbers) | `dist/` (checked) | 2 | 0.01 / 0.02 | -0.18 / -0.16 | ok |
| HashSet.remove + add (numbers) | `dist/` (checked) | 2.67 / 2 | 2.26 / 2.42 | 0.96 / 0.88 | ok |
| Queue.enqueue + dequeue | `dist/` (checked) | 2.27 / 1.5 | 1.85 / 2.06 | 1.60 / 1.48 | ok |
| Queue BFS shape (dequeue 1, enqueue 2; reported) | `dist/` (checked) | — | 1.02 / 1.14 | 1.21 / 1.09 | reported |
| Stack.push + pop | `dist/` (checked) | 1.5 | 0.79 / 0.90 | 0.02 / 0.02 | ok |
| Dictionary.get (record keys, reported) | `dist/` (checked) | — | 48.12 / 54.08 | 27.21 / 19.27 | reported |

**HashSet vs the 2.x runtime `HashSet`** (the reported row "3.0 class vs the 2.x runtime HashSet"): Bun -1.62 ns (3.0 is faster), Node.js +5.53 ns (3.0 is slower). Spec 011 amends the earlier "unchanged or faster than 2.x" rule to "measured and reported": the figure is informative, not a gate. A `Set`-backed store inside `HashCore` is a post-3.0 optimization follow-up.

Sizes (FR-043, minified + gzipped, from `dist/`), against NFR-008 (the collection rows raised to the measured size + 10% by W4-25 (d): FR-016's exception classes, about 0.97 KB, plus `elementEquals` / `defaultEquals`, about 0.5 KB, shared when several collections are used; slimming the exceptions is a follow-up):

| Entry | Budget | bun | esbuild |
|---|---|---|---|
| `List` alone (full surface) | 4586 B | 4099 B | 4057 B |
| `Dictionary` alone (with the views) | 4506 B | 4052 B | 3995 B |
| `HashSet` alone | 4283 B | 3859 B | 3810 B |
| `Queue` alone | 3128 B | 2834 B | 2787 B |
| `Stack` alone | 2947 B | 2634 B | 2578 B |
| `List` + `Dictionary` + `HashSet` together | 7112 B | 6401 B | 6302 B |
| `isType` + `getRuntimeType` + one class descriptor (011b row) | 1792 B | 1792 B | 1761 B |

## Immutable collections: repeated `Add` and shared storage (spec 011 stage 011h, FR-054)

`ImmutableList`, `ImmutableHashSet` and `ImmutableDictionary` are copy-on-write with tip-append sharing (research R18, decision W4-11): several versions share one append-only store, each version sees only the entries it was built with, and an `Add` of a new element on the newest version appends in place (amortized O(1)); every other change, and an `Add` on an older version, copies (O(n)). **Memory:** an old version keeps the whole shared store alive, including what newer versions appended after it; this is a memory cost, not a semantic one (no version ever sees another's elements). Bulk construction should still go through a builder or `CreateRange`.

FR-054's budget, building 1e4 elements by repeated `Add` (`x = x.Add(i)`), ≤ 50 ms normalized as NFR-001 does (`bun run bench:immutable`, `bun run bench:immutable:node`; checked `dist/`, median of 11 runs after 5 warm-ups). Measured 2026-11-19 on Apple M1 Pro:

| Class | Node.js 24.11.0 raw / normalized (ms) | Bun 1.4.2 raw / normalized (ms) | Verdict |
|---|---|---|---|
| `ImmutableList` | 3.01 / 3.19 | 0.85 / 0.73 | ok |
| `ImmutableHashSet` | 3.84 / 4.06 | 1.20 / 1.03 | ok |
| `ImmutableDictionary` | 6.52 / 6.91 | 1.75 / 1.50 | ok |

C_measured 0.557 ns (Node.js, C_ref 0.59 ns) and 0.548 ns (Bun, C_ref 0.47 ns). Plain copy-on-write measured 2.6 s (Node.js) / 0.25 s (Bun) for the hashed classes (research R18).
