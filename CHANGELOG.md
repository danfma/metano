# Changelog

All notable changes to Metano are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased (3.0.0 migration notes)

Pre-release notes for the 3.0.0 breaking changes that are already merged;
the release regenerates the released sections, and these notes carry forward
the migration list.

### Tree-shakable generated code (spec 011 FR-040, FR-043, FR-044, #335)

- A `[Branded]` / `[InlineWrapper]` companion is no longer a TypeScript `namespace` (an IIFE every bundler keeps): its members are module-level functions and the companion is one pure object literal of them, `export const Money = { create, fromValue } as const;` beside `export type Money`. Call sites (`Money.create(x)`) and imports are unchanged; hand-written TypeScript that merged declarations into the companion namespace must use the object instead.
- A nested type is a module-level class (`class Owner$Maker`) exposed through a static getter of its owner (`static get Maker() { return Owner$Maker; }`), and the type `Owner.Maker` is kept by an erased `declare namespace`. `Owner.Maker` keeps working as a value and as a type; the owner's own file names `Owner$Maker` directly. Nested enums, interfaces, delegates and generic and nested-of-nested types follow the same shape; a nested type now loads before an owner whose static initializer constructs it.
- A static initializer that calls a companion declared later in the same file (`static readonly Default = Money.create(0)` above the `Money` companion) now throws a `ReferenceError` at load, where it threw a `TypeError` before.
- The runtime `UUID` and `ImmutableCollection` take the companion shape (`UUID.newUuid()` is unchanged).
- A numeric enum is an `as const` object plus a type alias instead of a TS `enum`: the closed union of its values, or for a `[Flags]` enum the brand `number & { readonly __enum?: "E" }`; reverse lookups (`E[0]`) no longer exist.
- Static state is annotated `/* @__PURE__ */` (JSON-context defaults, static-constructor slots, overload tokens, static field initializers, side-effect-free module state), so a bundle drops a type nothing uses; a pure-annotated static field asserts its type (`= /* @__PURE__ */ new Ctx() as Ctx`) so tsgo and Biome keep the annotation.
- `metano-runtime` evaluates nothing a bundler must keep at load: a side-effect-only import of any of its modules, the root included, bundles to 0 bytes under bun, esbuild and Rollup, and so does every golden of the test suite (CI gates both).

### `dynamic` (spec 011 FR-030, #335)

- Operations on a `dynamic` receiver keep their C# member names verbatim (`d.AppName`, `d.Run()`), where 2.x camelCased them (`d.appName`); `ToString` / `GetHashCode` / `Equals` / `GetType` keep the `object` lowering. Type arguments of a dynamic generic call are dropped.
- Explicit numeric casts of a `dynamic` value convert it at run time through `metano-runtime` helpers (`numberFromDynamic`, `bigintFromDynamic`, `charFromDynamic`, `decimalSourceFromDynamic`), which throw `InvalidCastException` for `null` or a non-numeric value; 2.x emitted the bare value. A cast to a class is checked with `castTo` as from `object`.
- A call of a typed receiver with a `dynamic` argument binds to its single candidate (with its mapping, qualifier and name) or to the overload group's runtime dispatcher; 2.x emitted an unmapped, sometimes unqualified call (`Console.writeLine(d)`, `sOv(d)`).
- Operators with a `dynamic` operand are plain JavaScript operators (`d + 1L` adds `1`, `x == d` on a `double` is `===`); a compound assignment to a typed target still converts the result back to the target's type (`i += d` wraps to 32 bits, `c += d` stores a code unit).
- An explicitly typed `dynamic` local is annotated `: any`.
- New error `MS0053` (TypeScript) for what cannot pass through (initializers on a late-bound `new`, named or `ref` arguments on a dynamic call, user-defined operators and conversions bound at run time, `Console.WriteLine(d)`-style calls over several candidates). See `docs/runtime-types.md` § dynamic.

### Member access on `AsDynamic()` (spec 011 FR-036, #335)

- `x.AsDynamic().Member`, `.Member()`, `?.Member`, `[i]` and their assignments, compound assignments and `++` are now `dynamic` accesses with verbatim names (`(x as any).Member`), where they reported the interim `MS0001`. Dart keeps the `MS0001` until after 3.0.0.
- New warning `MS0057` when the static type of `x` emits `Member` under another name (`Total` is `total`, or a `[Name]` rename), since the verbatim access reads `undefined`.

### LINQ aggregates and set operators (spec FR-056 to FR-058)

- `Min` / `Max` / `MinBy` / `MaxBy` over empty or nullable sequences, and `Sum` / `Average` / `Min` / `Max` over `decimal`, `string` and other non-`number` elements, now match .NET (an empty `Max()` throws instead of `-Infinity`; `[null, 4].Min()` is `4`; `decimal` sums are decimal.js sums). Strings order by code unit, not by culture (#618, #657).
- `Union`, `Intersect`, `Except` and the static call form `Enumerable.Where(xs, p)` lower to the pipe form; the legacy `Enumerable.from(...)` mappings are gone (#657, #619).
- A lambda parameter of type `decimal` is annotated `Decimal`, no longer `number`.

### `char` as a UTF-16 code unit (spec FR-071 to FR-073, #501)

- A `char` read as a number now reads its code unit: `int i = c`, `(int)c`, `c + 1`, `c - 'a'`, `c < n`, `l << c`, `(byte)c`, `decimal d = c`, an `M(int)` / `M(string)` overload argument, `foreach (int x in str)` and `[.. str]` into a numeric collection all print `c.charCodeAt(0)` where they printed the string (and so concatenated or gave `NaN`). A `char` literal read as a number folds to its code.
- A `char` made of a number (`(char)n`, `(char)(c + 1)`) and `c++`, `c--`, `c += (char)2` store back through `String.fromCharCode(... & 0xFFFF)`; a checked context throws `OverflowException`. Text positions (`"x" + c`, interpolation, `ToString`, `char` vs `char` comparison, `switch` on a `char`) are unchanged.
- An unpaired UTF-16 surrogate in a string or `char` literal prints as a `\uXXXX` escape (TypeScript and Dart; `{"\ud83d"}` in JSX), where the CLI aborted with `EncoderFallbackException`; a write failure now reports `MS0001` naming the file.
- Dart output does not change; char arithmetic in Dart remains a documented gap.

### Static constructors run lazily, at first use (spec FR-066, #681)

- A type that declares a static constructor no longer runs it in a class `static { }` block at load. A `static readonly #$init = typeInitializer("Name", () => { … })` guard runs the static field initializers and then the body, once, right before the first instance creation or static member access, as .NET does. A base class whose static constructor constructs a derived class now loads.
- Its static fields and auto-properties become `#name$` slots behind `static get` / `static set` accessors that run the guard. A `static readonly` member the static constructor writes keeps a `private` setter (2.x declared it mutable and public). Reference slots start at `null`.
- A failing static constructor throws a `TypeInitializationException` (new runtime class, `catch` tests it) wrapping the original, and every later access rethrows it.
- Types without a static constructor keep their eager static field initializers.
- A struct with a static constructor runs it, then its instance field and auto-property initializers, in every declared constructor, also one called with an empty `params` array; its implicit default value runs neither.
- A static member whose TypeScript name equals a slot's name (a private field `count` beside a property `Count`) is an `MS0001` error asking for a `[Name]`.
- The generated barrel lists a derived module before the base module whose static constructor constructs it, and `[EmitInFile]` prints a base class before a class in the same file that extends it.

### ⚠ `int` / `uint` arithmetic wraps in the default context (spec FR-074 to FR-076, #639)

- In an unchecked context, `int` / `uint` `+`, `-`, `*`, unary `-` and the stores of `++`, `--`, `+=`, `-=`, `*=` are reduced modulo 2^32 as .NET does, with native operators: `(a + b) | 0`, `(u - v) >>> 0`, `Math.imul(a, b)`, `i = (i + 1) | 0`. `int.MaxValue + 1` is `int.MinValue` (it was `2147483648`). This closes, for TypeScript, the known gap listed under the integers of 32 bits or fewer below: an unchecked result read by checked code is in range, and `(int)(i + j)` reads the wrapped value.
- No reduction where C# proves the value fits (operand ranges, a `char` operand, array and `string` `Length`, the `Count` of the BCL collections that own their storage, the step of a bounded `for` counter, which keeps `i++`), in either context; a JavaScript consumer that reduces modulo 2^32 itself (`&`, `|`, `^`, `~`, shifts, `Math.imul`, another wrap, an unchecked narrowing) drops its operand's trailing `| 0`.
- `counts[key]++` and `counts[key] += n` on a `Dictionary<_, int>` now report an `MS0001` error in the default context (they emitted invalid `counts.get(key)++`, which `tsc` rejected, so the build still fails); the checked and narrow-type forms keep their `MS0001` warning.
- Generated output changes wherever such an operation can leave the range; an accumulator that overflows on most iterations is about 10 times slower on Node than the asm.js form (benchmark in spec FR-076). Dart output does not change: its unchecked `int` / `uint` arithmetic keeps the 64-bit result (deferred to after v3).

### Integers read as `decimal`, and `decimal` stores (baseline FR-080, #476)

- An `sbyte`, `byte`, `short`, `ushort`, `int` or `uint` read as a `decimal` (an assignment or initializer, an argument, a `return`, an operand of `+`, `<`, `==`, ..., a collection element, a conditional branch, the left operand of `??`, the cast `(decimal)i`) is now `new Decimal(i + 0)` (the `+ 0` turns a JS `-0` into `0`), lifted `n == null ? null : new Decimal(n + 0)`. It printed the `number`, which failed `tsc` (`TS2322`) and threw `TypeError` at the first `Decimal` method call.
- `d += x`, `-=`, `*=`, `/=`, `%=` and `d++` / `--d` on a `decimal` store the `Decimal` method call (`d = d.plus(new Decimal(x))`), evaluating the target once; the native operators concatenated strings (`a += d`) or stored a `number` (`a++`).
- Dart output does not change.

### ⚠ 64-bit integers become `bigint` (spec 012)

- `long` / `ulong` (and `nint` / `nuint`) map to TypeScript `bigint` instead
  of `number`; hand-written TS that passes `number`s fails `tsc` (pass `5n` /
  `BigInt(n)`, convert with `Number(x)`). TypeScript `target` ES2020+ is
  required for `bigint` literals. It is the only mapping: the compiler has no
  option that keeps the 2.x `number` output, and the first build with 3.0
  discards the incremental cache of a 2.x build.
- Overflow now wraps (or throws under `checked`) exactly where C# does;
  `/` by zero throws `DivideByZeroException` for `long` too.
- JSON: 64-bit values serialize through the generated context as exact
  digits (`rawJSON` leaves in the tree); plain `JSON.stringify` on a
  transpiled object with a 64-bit field throws. A tree holding a `number`
  beyond the safe integers throws and points at `deserializeText`, which
  reads every 64-bit value from its exact token (`1.0` and `1e3` throw, as
  .NET throws); 64-bit dictionary keys read back as `bigint` keys.
- Runtime helpers that keep their name and change meaning: `isInt64` /
  `isUInt64` are `(value: unknown) => value is bigint` over the whole `long`
  / `ulong` range (2.x: a safe-integer `number`), so hand-written TS that
  calls them with a `number` now gets `false`; `tryParseInt64` returns
  `[found, bigint]` and accepts an optional `NumberStyles` value (2.x: a
  `number`). `hashInt64` is the .NET word fold every `bigint` hash goes
  through — `BigInteger` hash values computed in 2.x no longer match. The
  pre-release names `isInt64Value`, `isUInt64Value` and `tryParseInt64Value`
  never shipped in a release and are gone.
- `BigInteger` casts go through the same conversion helpers as the 64-bit
  ones: `(decimal)big` is `new Decimal(decimalTextFromBigInteger(big))` and
  `(BigInteger)dec` is `bigIntegerFromDecimal(dec)` (2.x: `big.toString()` /
  `BigInt(dec.toFixed(0))`, which rounded where C# truncates), an `int`
  converted to `BigInteger`, implicitly or by a cast, is `BigInt(i)` (2.x
  emitted the bare `number` for the implicit one), and `(int)dec` truncates
  before it reads the number (`dec.trunc().toNumber()`). The narrowing and
  floating-point casts of a `BigInteger` — `(int)`, `(uint)`, `(short)`,
  `(byte)`, `(double)`, `(float)` — and `(BigInteger)someDouble` were erased
  in 2.x; they call runtime helpers now (`int32FromBigInteger`,
  `doubleFromBigInteger`, `bigIntegerFromDouble`, …), which throw
  `OverflowException` where .NET does.
- `new T[n]` is supported now and emits an array filled with the element
  default: `0` / `0n` / `new Decimal("0")` for numbers, `false`,
  `"\u0000"` for `char`, `UUID.empty` for `Guid`, the zero member of an
  enum (`0 as E` when it declares none), and `null` for a string, a class,
  a nullable type or a type parameter constrained to `class`. 2.x reported
  `MS0001`, which other struct elements and unconstrained type parameters
  still do.
- Tuples and collection-expression spreads convert element by element where
  C# does: `(long, long) t = intPair;`, an `(int, int)` returned or passed
  as a `(long, long)`, `wide == narrow` / `wide.Equals(narrow)` between the
  two, and `List<long> l = [.. ints];` (also `HashSet<long>`, `long[]`,
  nested and nullable tuples) emit `[BigInt(p[0]), BigInt(p[1])]` /
  `...Array.from(ints, (e) => BigInt(e))`; a `foreach ((long, long) p in
  intPairs)` rebuilds each element the same way, and a cast tuple literal
  (`((long, long))(a, b)`) converts each element where it is written, in
  source order. In TypeScript, an explicit tuple conversion throws for a
  missing value, as .NET does, where a nullable tuple (`((long,
  long))nullablePair`, `foreach ((long, long) p in nullablePairs)`, nested
  ones included) or a nullable numeric element that crosses a lowered
  numeric conversion (`int?` to `long`) converts to a non-nullable one. A
  cast-literal element is unwrapped whatever nullable analysis says about
  it (only a value wrapped in place, `(int?)2`, converts without the
  check), and so is the cast of a whole nullable tuple, which follows the
  scalar cast (#629, below). A nullable element whose conversion lowers to
  nothing (`int?` to `int`, as
  in `((int, long))(int?, int)`, or `long?` to `long`, where the `null`
  lands in a `bigint` slot and fails later with a `TypeError`) and a
  nullable nested tuple with no converting element still keep `null`
  (#630). A converted string spread
  (`List<long> codes = [.. text];`) reports `MS0001` in TypeScript; the Dart
  target does not report it (#613).
- TypeScript: `x.Value` and `(T)x` on a `Nullable<T>` now always carry the
  missing-value throw, including after a `HasValue` / `!= null` check, after
  `!`, and in nullable-disabled code. Code that relied on an unguarded read
  of a missing value now throws `Error('Nullable object must have a
  value.')`, as .NET does. Use `GetValueOrDefault()` or `??` where a default
  is intended. Only a value wrapped where it is written (`((int?)5).Value`,
  `(int)(int?)x`) is read without the check. Dart output is unchanged: it
  still drops the check where nullable analysis calls the value present
  (#629).
- An operator group whose overloads return different types (`int operator
  +(M, int)` beside `long operator +(M, long)`) declares its dispatcher
  implementation as `any`, as method dispatchers do; 2.x declared the first
  overload's type, which did not type-check. Overloads that return one type
  keep it, and method and module dispatchers now tell that the same way: a
  group whose overloads all return one constructed type
  (`Task<OperationResult<Issue>>`) declares it on the implementation
  (`Promise<OperationResult<Issue>>`) where it declared `Promise<unknown>` /
  `any`.
- `FirstOrDefault`, `LastOrDefault`, `SingleOrDefault` and `List<T>.Find`
  return `default(T)` on a miss for a non-nullable value element (`0`, `0n`,
  `false`, `new Decimal("0")`, the enum's zero, `UUID.empty`), and the
  `defaultValue` overloads pass their argument: `longs.FirstOrDefault() + 1`
  is `1n` (#601). 2.x returned `null`. An element whose default no literal
  spells (a user struct, a tuple, `KeyValuePair` (so `dict.FirstOrDefault()`),
  `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, an
  `[InlineWrapper]` struct, a `struct` type parameter, a `[StringEnum]`
  without a zero member) still gets `null`, now with an `MS0001` warning
  (#621); so does, without a warning, a type parameter
  that is not constrained to `struct` (unconstrained, `notnull`,
  `IComparable<T>`, `Enum` alone). `ElementAt` / `ElementAtOrDefault` /
  `DefaultIfEmpty` have no lowering yet (#617), `Min` / `Max` / `MinBy` over
  an empty or nullable `number` sequence still differ from .NET (#618), and
  the static call form `Enumerable.FirstOrDefault(xs)` is not lowered
  (#619). A default value typed wider than the source element
  (`dogs.FirstOrDefault(animal)`) runs correctly but does not type-check in
  the generated TypeScript.
- `metano-runtime`: `firstOrDefault`, `lastOrDefault` and `singleOrDefault`
  are overloads typed by their `defaultValue` (it was `T | null = null`):
  no default gives `T | null`, a `T` default gives `T`, and a nullable or
  optional default (`T | null`, `T | undefined`) gives `T | null`.
  Hand-written calls inside `linq(...)` keep compiling, with or without an
  explicit element type argument, with one exception: a default of a wider
  type than the elements (`linq(dogs, firstOrDefault(undefined, animal))`)
  needs the explicit type argument (`firstOrDefault<Animal>(undefined,
  animal)`). The default value no longer infers the element type, so a
  detached `firstOrDefault(undefined, 0)` is a term over `unknown` (write
  `firstOrDefault<number>(undefined, 0)`), and a default of an unrelated
  type is rejected. The declarations use `NoInfer`, so type-checking against
  `metano-runtime` needs TypeScript 5.4 or later; 5.3 with `skipLibCheck:
  true` reports nothing and silently reads `NoInfer` as `any`.
- Known gaps a 2.x project can meet after the upgrade: tuple and spread
  elements that convert to `decimal` are not converted, and the Dart target
  leaves the ones that convert to `double` or `BigInteger` (#606);
  conversion operators of one type still share a dispatcher that only
  reaches the first (#600).
- `[MapMethod]` / `[MapProperty]` lose the hidden `WhenInt64IsBigInt` named
  argument (it was honored only in Metano's own mappings).
- Interop declarations (`[Import]`, `[Emit]`, `[External]`, user
  `[MapMethod]` / `[MapProperty]` templates) exchange `bigint` with
  JavaScript; the compiler warns `MS0065` at each one.
- Overload dispatch decides `M(int)` vs `M(long)` by `typeof`; `object o =
  5UL; o is long` is now `true` (a raw `bigint` reads its range).
- `uint op int` promotes to `long` in C#, so those expressions produce
  `bigint` intermediates even in code that never names `long` (`u == i`
  emits `BigInt(u) === BigInt(i)`); so does an `int` passed to a BCL
  parameter that is a `long` (`new TimeSpan(i)`,
  `TimeSpan.FromMilliseconds(i)`, `DateTimeOffset.FromUnixTimeSeconds(i)`
  pass `BigInt(i)`). Floating-point → integer casts truncate now instead of
  being erased (`(int)3.7` is `3`).
- For authors of other targets: the core always produces
  `IrNumericConversion`, `IrIntegerOverflow`, folded 64-bit literals,
  `IrArrayCreation` with its element default, and the element conversion of
  an `IrSpreadExpression`; no extraction option turns them off. Both nodes
  also carry the integers of 32 bits or fewer (next section): an
  `IrNumericConversion` between two of them is a narrowing, and an
  `IrIntegerOverflow` may name any integer kind.
- The cost is documented and benchmarked (`bun run bench:int64`); code that
  does not need 64-bit range should keep `int` or `double`.

### ⚠ Integers of 32 bits or fewer narrow and check like C# (spec 012 FR-074–FR-077, #639)

`sbyte`, `byte`, `short`, `ushort`, `int` and `uint` stay JavaScript
`number`s. 2.x erased every conversion among them and never checked them;
3.0 lowers them in two layers:

- **Narrowing conversions (unchecked context)** keep the low bits with the
  native operators, with no runtime call: `(int)u` → `u | 0`, `(uint)i` →
  `i >>> 0`, `(short)i` → `(i << 16) >> 16`, `(ushort)i` → `i & 0xFFFF`,
  `(sbyte)i` → `(i << 24) >> 24`, `(byte)i` → `i & 0xFF`. `(uint)-1` is
  `4294967295` (2.x: `-1`) and `(byte)300` is `44` (2.x: `300`). A
  conversion that cannot change the value (a widening, the same type, an
  operand such as `x & 0xFF`) still emits nothing, and a constant is its
  folded literal.
- **Compound assignments and `++` / `--` on `sbyte`, `byte`, `short` and
  `ushort`** store the narrowed value, as the conversion C# inserts does:
  `b += 1` emits `b = (b + 1) & 0xFF`, on locals, fields, properties,
  indexers and array elements. `byte b = 255; b++;` is `0` (2.x: `256`).
- **`uint` bit patterns:** the result of `&`, `|`, `^`, `~` and `<<` on a
  `uint` is read back as unsigned (`(a | b) >>> 0`; 2.x returned a negative
  number when bit 31 was set), `>>` on a `uint` is the logical `>>>`, and
  `>>>` on an `int` yields an `int` (`(i >>> n) | 0`).
- **Checked contexts** (`checked(...)`, `checked { }`, the project's
  `CheckForOverflowUnderflow`) now throw `OverflowException` where C# does:
  a narrowing conversion calls `checkedInt32` / `checkedUInt32` /
  `checkedNarrowNumber`, and `+`, `-`, `*`, unary `-`, `++`, `--` and their
  compound forms on `int` / `uint` pass their result through `checkedInt32`
  / `checkedUInt32` (`i++` becomes `i = checkedInt32(i + 1)`). 2.x never
  threw. A project compiled with `CheckForOverflowUnderflow` therefore gets
  a `metano-runtime` call on every such operation. An `unchecked` block
  keeps the plain operators, but its results are not wrapped (the known gap
  below), so do not let a value that may have overflowed there reach a
  checked operation.
- **Checked conversions of nullable values** are checked now, for the
  64-bit integers too. Roslyn reports the checked flag for a plain numeric
  conversion only, so these were all emitted unchecked before:
  `checked((byte?)ni)`, `checked((int?)l)`, `checked((int)nl)`,
  `checked((int?)nullableDouble)`, `checked((ulong?)ni)`,
  `checked((long?)nullableULong)`, `checked((ulong?)nl)`.
- **Known gap:** `int` / `uint` arithmetic does not wrap in the default
  (unchecked) context. `int.MaxValue + 1` is `2147483648` where C# gives
  `int.MinValue`; values stay exact up to 2^53. Wrapping every integer
  expression (`(a + b) | 0`, `Math.imul(a, b)`) is deferred (#639, layer
  3). `int.MinValue / -1` returns `2147483648` instead of throwing. At the
  edges of the gap:
  - an unchecked overflow that feeds checked arithmetic throws: in a
    `CheckForOverflowUnderflow` project, `var h = unchecked(a * 31 + 7);
    return h + 1;` with `a = int.MaxValue` is `2147483625` on .NET and an
    `OverflowException` here, because `h` was never wrapped. Narrow the
    unchecked result through another type to wrap it
    (`unchecked((int)(uint)(a * 31 + 7))` emits `((a * 31 + 7) >>> 0) | 0`,
    exact while the value stays below 2^53);
  - a cast to the operand's own type emits nothing and does not wrap:
    `(int)(i + j)` and `(uint)(u1 - u2)` keep the unwrapped result, while
    `(uint)(i - j)` and `(int)(u1 + u2)` narrow;
  - `uint` bit patterns are normalized and `uint` arithmetic is not:
    `(a - b) | 0u` is C#'s value, `a - b` alone stays negative when it
    underflows;
  - `checked(a + a % b)` with `b == 0` throws `OverflowException` where
    .NET throws `DivideByZeroException` (the native `%` yields `NaN`).
- **Dictionary entries:** a compound assignment or an increment whose store
  needs one of these lowerings reports `MS0001` (`'counts[key]++' on a
  dictionary entry is not supported.`): `counts[key]++` or `counts[key] +=
  2` on a `Dictionary<string, int>` in a checked context, any such store on
  a `byte` / `sbyte` / `short` / `ushort` value, `flags[key] |= mask` on a
  `uint` value. The 64-bit forms already did. The compound shifts report it in
  the default context too: `d[k] <<= 1` and `d[k] >>>= 1` on an `int`
  value, `d[k] >>= 1` on a `uint` value. 2.x emitted `counts.get(key)++`
  for all of these, which is not valid JavaScript; a store on an `int`
  entry that needs no lowering (`counts[key]++`, `+=`, `>>=` in the default
  context) still does, and so does `>>>=` on a `uint` entry. Read, compute and write the
  entry in separate statements.
- Not covered: `char` conversions (#501), `decimal` sources (#521, #606),
  conversions to or from an enum, and `[Queryable]` expression trees keep
  their earlier lowering, with three visible exceptions: `checked(a - b)`
  on two values of an enum declared narrower than `int` is checked (C#
  types the difference as the underlying integer); `(byte)(int)c` on a
  `char` emits `c & 0xFF` on a string, which is `0` and does not type-check
  (TS2362, with or without `--strict`); and `checked(c + 1)`
  on a `char` now throws `OverflowException` where it returned a
  concatenated string. The last two wait for #501.
- `metano-runtime` adds `checkedInt32(value)`, `checkedUInt32(value)` and
  `checkedNarrowNumber(value, bits, signed)`.
- Dart: an `int` is 64-bit, so the same conversions and stores print
  `.toSigned(n)` / `.toUnsigned(n)` (`x.toUnsigned(8)`,
  `(b + 1).toUnsigned(8)`, `(i << n).toSigned(32)`,
  `(i.toUnsigned(32) >>> n).toSigned(32)`). A 32-bit shift count is not
  masked there, so any count outside 0..31, constant or not, differs from
  C# unless the whole shift is a constant expression (`a << 32` is `0`, C#
  gives `a`), and a negative count throws `ArgumentError`. Dart never throws in a checked
  context: checked `int` / `uint` arithmetic prints as its unchecked form
  (`checked(a + b)` → `a + b`, no wrap), and a checked conversion or narrow
  store prints the narrowing of its unchecked form.

### ⚠ Overload factories, fast paths and direct call sites (spec 013)

The full guide, naming rule included, is [docs/overloads.md](docs/overloads.md).

- Generated code calls the overload C# bound: `new Money(19.9m)` →
  `Money.fromAmount(…)`, `: base(amount)` → `super(Money.byMoneyAmount, …)`,
  `d.Add(1)` → `d.addX(1)`, `base.Add(1)` → `super.addX(1)`, operators →
  `Money.__addMoneyInt32(a, b)`, overloaded module functions and extensions →
  their exported fast paths, with omitted defaults passed explicitly. The
  dispatchers stay public for hand-written TypeScript, which is bypassed when
  it patches a generated constructor's or dispatcher's runtime branches:
  override the fast path instead, and implement the fast paths of a generated
  interface (TS2420).
- Static binding: `object o = "s"; F(o);` with `F(object)` / `F(string)` now
  calls `F(object)` in generated code, as C# does (the 2.x dispatcher chose by
  runtime type).
- New public members: per constructor overload a `protected static readonly`
  selector (`byMoneyAmount`) and a factory (`fromAmount`, with the overload's
  accessibility; `static override` when it keeps an inherited factory's name);
  per method overload an addressable fast path with its overload's
  accessibility (`override` / `abstract` where C# has them, declared on
  interfaces); exported module fast paths in the barrels. A derived class
  inherits the base factories it does not redeclare.
- Renamed fast paths (exact CLR spellings, root-anchored names): `fInt` →
  `fInt32`, `gLong` → `gInt64`, `wLong` / `wULong` → `wInt64` / `wUInt64`,
  `pickLong` / `pickULong` → `pickInt64` / `pickUInt64`, `sumIntArray` →
  `sumInt32Array`, `hIntOrNull` → `hInt32OrNull`, `identityValue` →
  `identityT`, `now` → `nowDefault`, `areaSide` → `areaS`; operator helpers
  `__addAcctLong` / `__addAcctULong` → `__addAcctInt64` / `__addAcctUInt64`
  (every `Int`, `Long`, `ULong`, `Bool`, `Float` spelling); duplicate 2.x
  names split (`uInt32` / `uUInt32`, `lListOfInt32` / `lInt32Array`,
  `kNumericsDecimal` / `kOtherDecimal`); a `From`-style fast path a factory
  claims takes `$` (`fromAmount$`).
- `[Name]` on one overload pins its suffix only: an operator overload's
  `[Name("scaled")]` (`__scaled`) becomes `__multiply` plus the fast path
  `__multiplyScaled`; a module or extension overload's `[Name("text")]`
  (`textS`) becomes `twiceText`; an interface overload's `[Name("side")]`
  (`side(s)` beside `area(w, h)`) becomes `area(s)` plus the fast path
  `areaSide`; a class method overload's `[Name("addText")]`, ignored in 2.x
  (`addS`), becomes `addAddText`. Conversion operators keep their `[Name]`
  (`__toInt`).
- Branded companions (`[Branded]` / `[InlineWrapper]`): every overloaded
  static group now exports one function per overload beside its dispatcher
  (`Tag.sL`, `Tag.sU` beside `Tag.s`; in 2.x they were namespace-local), and
  generated code calls the one C# bound: `Tag.S(new UId(1))` →
  `Tag.sU(UId.create(1n))`. This is new public surface on the namespace, and
  it fixes calls the dispatcher resolved to another overload (a branded value
  beside its primitive, `long` / `ulong`, `int` / `double`, `object` /
  `string`). The dispatcher is unchanged. A function of a non-public overload
  is tagged `/** @internal */`, and a name already taken in the namespace
  takes `$` with `MS0067`. An overloaded operator or conversion group keeps
  its per-overload functions local to the namespace: generated code does not
  apply the operators of a branded type through them yet (#599).
- `[JsonFactory]` on a method of an overloaded group is now `MS0034` (error):
  `[JsonFactory] From(string)` beside `[Name("num")] From(int)` used to
  generate a descriptor calling the dispatcher.
- New diagnostics: `MS0067` (warning: a generated name was taken and took
  `$`), `MS0068` (error: a user member takes a generated fast path or factory
  name), `MS0069` (warning: a generic interface with overload groups
  implemented more than once keeps runtime dispatch); `MS0003` also reports the
  naming dead end and invalid or ignored `[Name]` pins.
- `stripInternal` is unsupported for a package consumed by other Metano
  packages, whose generated code calls its `internal` factories and fast paths.
- Upgrade packages and consumers together: a 3.0 consumer with a project
  reference calls factories and fast paths a 2.x-built producer lacks; a
  metadata-only reference keeps the dispatcher calls. Regenerate library
  packages first: a consumer transpiled on 3.0 against the generated
  TypeScript of an older library fails to type-check (TS2339 on the missing
  member) and throws a `TypeError` at run time, on bound companion calls
  (`Tag.sU`) as on every other fast path.
- Dart (experimental): overloaded constructors become named constructors
  (`Dimension.fromSize`) and overloaded methods fast paths (`addX`), with
  direct call sites, instead of the primary overload alone; `[Name]` on an
  overload pins its suffix there too, and call sites spell a method's `[Name]`.

### ⚠ Catch clauses lower to one `catch` with a chain (spec 011 FR-015, partial; #628)

- A `try` with several catch clauses emits one `catch (e$)` that tests the
  caught value against each clause in source order; 2.x emitted one `catch`
  block per clause, which is a JavaScript syntax error.
- A typed clause now filters: a lone `catch (UserException)` runs only for
  that class (and the classes derived from it) and rethrows every other
  value. 2.x caught everything there.
- A `when` filter is honoured: the clause runs only when it holds, and the
  next clause is tried otherwise. 2.x ignored the filter.
- `throw;` rethrows the caught value (2.x emitted `throw null;`), also after
  the clause's variable was reassigned; Dart prints it as `rethrow;`.
- A clause on an exception type that is not transpiled (a BCL exception such
  as `catch (InvalidOperationException)`, or one of another library) tests
  `instanceof` the exception's `metano-runtime` class (see the next
  section, #651). It therefore does not catch a thrown value that is not an
  instance of that class (a string, a plain object, a plain `Error`, a
  cross-realm `Error` from an iframe, `node:vm` or a Jest sandbox), which
  `catch (Exception)` and a general `catch` still do.
- New `MS0001` warnings: a clause on a type parameter; an exception filter
  that declares a variable or passes an `out` argument. On Dart, which keeps
  its `on T catch` clauses, every exception filter is reported. A clause on
  a BCL exception type without a runtime class is the `MS0045` error of the
  next section.
- A filter runs after the `finally` blocks between the throw and the clause
  (.NET runs it before them). An exception thrown by a filter is treated as
  false (`tryFilter`, #651).

### ⚠ BCL exceptions are `metano-runtime` classes (spec 011 FR-013, FR-014, FR-016; #651)

- `new InvalidOperationException("x")` emits `new InvalidOperationException("x")`
  imported from its own module
  (`metano-runtime/system/exceptions/invalid-operation-exception`); 2.x emitted
  `new Error("x")`. The arguments pass by role in the runtime constructor's order, and the
  runtime class composes `message` as .NET does (`new ArgumentException("bad", "p")`, not
  `new Error("bad (Parameter 'p')")`; `new ArgumentOutOfRangeException("p", undefined, "m")`
  for .NET's `(paramName, message)` overload). The inner exception is the second constructor
  argument instead of the `{ cause }` option, and is still exposed as `cause`; a parameter
  named `inner` (`NotImplementedException`, `MissingMethodException`) now counts, where 2.x
  dropped it.
- The mapped set is the runtime's exception hierarchy: `Exception`, `SystemException`,
  `InvalidOperationException`, `ArgumentException`, `ArgumentNullException`,
  `ArgumentOutOfRangeException`, `KeyNotFoundException`, `IndexOutOfRangeException`,
  `FormatException`, `ArithmeticException`, `OverflowException`, `DivideByZeroException`,
  `NotSupportedException`, `NotImplementedException`, `InvalidCastException`,
  `NullReferenceException`, `MissingMethodException`, `ObjectDisposedException`,
  `OperationCanceledException`, `TaskCanceledException`, `TimeoutException`,
  `System.Text.Json.JsonException` and `SwitchExpressionException`, declared with
  `[ExportFromBcl]` in `Metano/Runtime/Exceptions.cs` (a project can map another exception to
  its own class the same way; such a class takes `(message, innerException)`).
- A user exception deriving from a BCL exception extends its runtime class
  (`class InvalidSlugException extends ArgumentException`), and a BCL exception in type
  position names it (`error: Exception`, 2.x `error: Error`). Hand-written TypeScript that
  passes a plain `Error` where a C# `Exception` parameter is typed no longer type-checks
  (`new Exception(error.message, error)` wraps one); at run time an overload dispatcher still
  accepts it.
- `error.name` is the C# type name (`"InvalidOperationException"`, and a user exception's own
  name, `"UnsupportedCurrencyException"`; 2.x `"Error"` for every exception), and
  `String(error)` / `error.toString()` is .NET's format with the full type name
  (`Shop.UnsupportedCurrencyException: …`, 2.x `Error: …`). Generated exception classes declare
  `override readonly name` (unless they declare a `Name` member of their own) and override the
  runtime's `$typeName` / `$typeNamespace` getters for this.
- `instanceof Error` stays `true` for every exception. `is` / `as` / type patterns, switch arms,
  overload dispatchers and `catch` clauses test `instanceof` the runtime class, so they tell
  BCL exceptions apart: `ex is ArgumentException` is `false` for an
  `InvalidOperationException`, `catch (OverflowException)` no longer takes the
  `FormatException` that `long.Parse("zz")` throws, and a BCL-typed clause ahead of another
  clause no longer reports `MS0001`. Tests on `System.Exception` itself stay
  `instanceof Error`, so a JavaScript error is an `Exception`; `catch (Exception)` and a general
  `catch` still take every thrown value. Known limitation: an overload dispatcher tries its arms in declaration
  order, so with `F(Exception)` declared before `F(ArgumentException)` a call from hand-written
  JavaScript takes the first arm for an `ArgumentException` (C# calls bind statically; user
  classes behave the same in 2.x).
- **A typed clause takes a JavaScript error as .NET would** (resolved by `toException`, spec 011
  FR-015, #651). In 2.x every BCL-typed clause tested `instanceof Error`, so
  `catch (NullReferenceException)` around a `null` dereference took the `TypeError` by accident;
  the first #651 slice made it reach `catch (Exception)` or a general `catch`; now the chain
  normalizes the caught value once (`const e0 = toException(e$);`) and the arms test and bind
  `e0`: a `TypeError` is a `NullReferenceException`, an `AbortError` a `TaskCanceledException`,
  any other `Error` (a `RangeError`, a `SyntaxError`) an `Exception` that keeps its `message` and
  `stack` and holds the original as `innerException` / `cause`, and a thrown non-`Error` value an
  `Exception` carrying its text. A `catch (Exception e)` that reads `e` gets the normalized
  value; `throw;` still rethrows the original. A `when` filter runs through `tryFilter`, so a
  filter that throws counts as `false`. A `TypeError` thrown on purpose by third-party JavaScript
  is also a `NullReferenceException`. Still open: Dart's `is` chain and the `TsTryStatement` node.
- A BCL exception outside the mapped set (`IOException`, …) reports `MS0001` where it is
  constructed and is emitted as `namedException("System.IO", "IOException", message, inner)`:
  a runtime `Exception` whose `Message` is .NET's (2.x too) and whose `name` / `toString()`
  report the C# type. A user exception deriving from one extends `Exception` and reports
  `MS0001` at its base list (its .NET ancestors' clauses do not take it). An `is` / `as` /
  pattern / `catch` clause on one is the new `MS0045` error (`UnanswerableRuntimeTypeTest`):
  testing `Exception` instead would take every exception.
- The argument guards lower to runtime functions with .NET's messages and the
  `[CallerArgumentExpression]` parameter name: `ArgumentNullException.ThrowIfNull`,
  `ArgumentException.ThrowIfNullOrEmpty` / `ThrowIfNullOrWhiteSpace` and the
  `ArgumentOutOfRangeException.ThrowIf*` comparisons. A comparison guard maps only for the
  values the runtime compares as .NET does: the sign guards for numbers and `char` (the sign
  bit: `-0`, a `decimal` `-0.0m` and `NaN` are negative); the ordered ones also for `bool` and
  the date and time types; `ThrowIfEqual` / `ThrowIfNotEqual` for those, strings, `Guid`,
  enums, transpiled structs, record structs and sealed records (generic ones included, each type argument read as the type it names) whose fields are such values all the way down, read from source (`[PlainObject]` ones included) and tuples of them,
  compared by value. Over another `T` (an ordered `string` or `Guid`, a class, an array,
  `object`, `DateTimeOffset` for equality, a type parameter, a record or struct holding one,
  a generic record with a class argument) it reports `MS0001`. Known gap: `Guid.Parse` keeps the text's letter case,
  so two `Guid`s parsed from differently cased text compare unequal. The message
  texts are .NET's, but values quoted inside a message are formatted by JavaScript, not .NET (`true` for `True`, an ISO date for `1/2/2024`, `PT5M` for a `TimeSpan`'s `00:05:00`, `1e+21`, `[object Object]` for a record). Any other static member of an exception
  type (`ObjectDisposedException.ThrowIf`) reports `MS0001`, as in 2.x.
- The runtime throws its own classes everywhere it reproduces a .NET failure: 32-bit integer
  division by zero (`DivideByZeroException`), `decimal.Parse(s)` and `decimal.Parse(s, provider)`
  (`FormatException` / `OverflowException`, 2.x decimal.js's `DecimalError`, and no overflow
  check; the `NumberStyles` and span overloads keep 2.x's `new Decimal(s)`), the `DateOnly`
  / `TimeOnly` / `DateTime` component constructors (`ArgumentOutOfRangeException`, 2.x
  Temporal's `RangeError`, which also accepted year 0), a negative array length
  (`OverflowException`, 2.x `RangeError`), a non-exhaustive switch expression
  (`SwitchExpressionException`, an `InvalidOperationException`, with .NET's message and the
  unmatched value; 2.x `Error("Non-exhaustive switch expression")`), `string.Replace` with a
  null or empty old value, `Dictionary.Add` with a null or duplicate key
  (`ArgumentNullException` / `ArgumentException`), the LINQ terminals
  (`InvalidOperationException`, now with .NET's messages: `First()` on an empty sequence says
  `Sequence contains no elements`, not `… no matching element`) and `Math.Round` /
  `decimal.Round` with an invalid digit count or mode (`ArgumentOutOfRangeException` /
  `ArgumentException` with .NET's messages, 2.x `RangeError`). `JsonException` extends
  `Exception` (2.x `Error`) and lives in `metano-runtime/system/exceptions/json-exception`
  (still exported from `metano-runtime/system/json`).
- Still pending (they throw nothing or a JavaScript error, as in 2.x, so a typed clause does not
  take them): indexers out of range (`List<T>`, arrays, `string`), a `Dictionary` indexer on a
  missing key, `Substring` / `PadLeft` / `new string(c, n)` with bad arguments,
  `Enumerable.Range` / `Repeat` / `ElementAt` / `Max` on bad input, `Guid.Parse`, decimal
  division by zero and narrowing conversions of a `decimal`; the unmapped parsers
  (`int.Parse`, `double.Parse`, `Enum.Parse`, `DateTime.Parse`, `DateOnly.Parse`,
  `TimeSpan.Parse`, `Convert.ToInt32(string)`) report `MS0001`.
- Dart output is unchanged except where the call itself changed for every target: a call to a
  method or constructor with a `[CallerArgumentExpression]` parameter now passes the caller's
  text (`ArgumentNullException.throwIfNull(x, 'x')`), and an exception constructor whose inner
  parameter is named `inner` no longer binds its arguments to temporaries. A `$` in a Dart
  string or `char` literal is now escaped (2.x emitted it bare, which Dart reads as an
  interpolation).
- The runtime's value equality (`valueEquals` / `valueHashCode`) now tells `TimeSpan` values
  apart: before, any two durations compared equal and hashed alike. This changes a record's or
  `[PlainObject]` record's `equals` and `==`, and `HashSet<Record>` and `Distinct` over records
  (which compare by value), when the records hold a `TimeSpan`: equal lengths are equal (`PT5M`
  and `PT300S`), and a duration with weeks, months or years, read from ISO text, compares field
  by field. Unchanged, by reference: `Dictionary<Record, _>` keys, and `HashSet` / `Dictionary` /
  `Distinct` over bare `TimeSpan`s (the runtime collections of spec 011 change that).
- A `[MapMethod]` template's generic type argument is imported only when the template embeds
  it as `$T<n>`, and its receiver only when the template embeds `$this` or names the
  receiver's type; before, a guard's `[PlainObject]` type argument was imported as a value
  and a static mapping called through a `using` alias imported the alias. A `using` alias of a
  type from another package imports the export under the alias
  (`import { Status as State }`).
- A call to a method or constructor with a `[CallerArgumentExpression]` parameter (baseline
  FR-052) passes the caller's argument text, as C# does; an omitted default before that
  parameter is passed as `undefined` in TypeScript when the callee is a transpiled,
  non-virtual method or constructor that applies the same default itself, and as the default
  the call site sees otherwise (an `[Import]` / `[External]` callee, a delegate, an interface
  or virtual member, whose implementation may declare another default). The text is also
  filled for extension syntax on a transpiled extension method, `base(...)` / `this(...)`
  initializers, an explicit `.Invoke(...)` and `[Emit]` callees (#676). 2.x
  passed `null`, and wrote a skipped enum default with a lower-cased member name
  (`Level.high`).

### ⚠ Unimplemented LINQ overloads report `MS0001` (baseline FR-017, partial; #648)

- A LINQ call the runtime operators do not implement used to transpile
  silently and run with a wrong result or throw; it now reports `MS0001` (a
  warning, so the build still succeeds unless warnings are treated as errors)
  at the method name and emits an expression that throws
  `Unsupported expression: …` at run time. The supported overloads are listed
  in [docs/bcl-mappings.md](docs/bcl-mappings.md#supported-overloads); a
  supported call emits the same TypeScript as before.
- Affected: another overload of a mapped operator
  (`Aggregate(seed, func, resultSelector)` dropped the result selector,
  `GroupBy(key, element)` ignored the element selector, `Aggregate(func)` and
  the tuple `Zip(second)` threw); a comparer argument, which every operator
  ignored (`OrderBy(k, comparer)` sorted ascending); a queryable
  `ToDictionary(k)`, whose tree landed in the value-selector slot and threw;
  and a `System.Linq` method nothing lowers, in the extension or the static
  form, which became a member call (`xs.Join(…)` returned the string of
  `Array.prototype.join`; `ToLookup`, `GroupJoin`, `SequenceEqual`, `Cast`,
  `ElementAt`, `AsQueryable` outside a chain threw).
- A comparer that is `null` only at run time (a parameter or variable) used
  to give the default comparison by accident and now reports and throws; a
  `null` or `default` literal comparer lowers as the comparer-less overload,
  as before, now without a type error.
- A queryable `Aggregate(seed, func)` no longer passes its expression tree to
  `aggregate`, which reads none (it ran, with a `TS2554` type error).
- Dart output and diagnostics are unchanged.

### ⚠ Array calls that C# 14 binds to `MemoryExtensions` (baseline FR-016; #657)

- C# 14 converts an array (and a string) to a span implicitly, so
  `array.Contains(x)`, `array.SequenceEqual(other)` and the other span
  extensions bind to `System.MemoryExtensions` instead of `System.Linq`. They
  were emitted by name, with no diagnostic. Most became member calls a
  JavaScript array does not have (`array.contains(x)`,
  `array.sequenceEqual(other)`), which failed `tsc` and threw at run time.
  Some ran with a wrong result: `array.Sort()` sorted numbers as strings
  (`[10, 9, 100]` gave `10,100,9`), `array.LastIndexOf(x, null)` and
  `text.EndsWith(x, null)` read the `null` comparer as a position, and
  `array.IndexOf(otherArray)` looked for the array as an element.
- Fixed: `array.Contains(x)` emits `array.includes(x)`, the output of
  `List<T>.Contains`. A comparer that is omitted or the `null` constant
  (`null`, `default`, a cast or a `const`) is dropped from every
  `MemoryExtensions` call (`array.IndexOf(4, null)` emits `array.indexOf(4)`,
  `text.EndsWith('b', null)` emits `text.endsWith("b")`), also when a named
  argument is written (`nullables.Contains(value: x)` emits
  `nullables.includes(x)`).
- Unchanged, now through declarative mappings: `array.IndexOf(x)` /
  `LastIndexOf(x)` (`indexOf` / `lastIndexOf`), the searches of a
  `ReadOnlySpan<char>` over a string (`StartsWith`, `EndsWith`, `IndexOf`,
  `LastIndexOf` with a char or a string, the parameterless `Trim` /
  `TrimStart` / `TrimEnd`, which become the string methods; also on a
  `string` receiver written as a cast, `((string)o).StartsWith(span)`),
  `Span<T>.Reverse()` (`reverse()`, in place) and `array.Sort(comparison)` /
  `span.Sort(comparison)` (`sort(comparison)`, as `List<T>.Sort`).
  `array.Reverse()` binds `Enumerable.Reverse` and is unchanged.
- Behaviour change: every other `MemoryExtensions` call reports `MS0001` (a
  warning) at the method name and emits an expression that throws
  `Unsupported expression: UnloweredSpanCall` at run time: a comparer that is
  not the `null` constant, a `StringComparison` overload, `SequenceEqual`,
  the parameterless `Sort()`, `Sort(comparer)`, `BinarySearch`,
  `IndexOfAny`, `ContainsAny`, `Count(value)`, `AsSpan`, `Trim(c)`, the static
  form, a string receiver such as `text.Count('a')`, and the string searches
  (`StartsWith`, `EndsWith`, the `Trim` family, the subsequence `IndexOf` /
  `LastIndexOf`) on an array (`char[]` included) or a `Span<char>`, which
  lower to JavaScript arrays (`array.IndexOf(otherArray)`,
  `chars.IndexOf("bc")`).
- `Sort()` reports for every element type. Its previous output,
  `array.sort()`, compared the elements as strings: it happened to sort
  right, on the inputs tried, for `char[]` and `bool[]` arrays, the temporal
  types (`DateOnly`, `DateTime`, `TimeOnly`) and small numeric enums, and
  was wrong for numbers from 10 up, `long`, `decimal`, `string` (culture
  order), string enums, `bool?` and `Guid`. All of them report until the
  runtime sort helper that picks the default comparison by element type
  lands (#663). Workaround: pass the
  comparison, `a.Sort((x, y) => x - y)` for numbers,
  `a.Sort((x, y) => x < y ? -1 : x > y ? 1 : 0)` for `char`,
  `a.Sort((x, y) => x == y ? 0 : x ? 1 : -1)` for `bool`.
- A lookup over a tuple element (`pairs.Contains((3, 4))`) lowers to
  `includes` and reports `MS0001`, as the `List<(int, int)>` lookup does: the
  tuples lower to arrays, compared by reference.
- Known limitations, shared with `List<T>`: `includes` and `indexOf` compare
  records, record structs, `decimal` and `DateTime` values by reference,
  `indexOf` / `lastIndexOf` do not find `NaN`, and `sort(comparison)` is
  stable where .NET's sort is not.
- Known limitations, emitted with no diagnostic as before: a
  `ReadOnlySpan<char>` over a `char[]` lowers to an array, so its
  `StartsWith` / `EndsWith` / `Trim` throw and `IndexOf("ab")` /
  `LastIndexOf("ab")` return `-1`; a string searched for a `char[]`
  (`text.IndexOf(chars)`, `text.StartsWith(chars)`) compares with the
  array's string form and answers wrongly; a `Memory<T>.Span` receiver
  throws (`Memory<T>` is not mapped).
- Dart output and diagnostics are unchanged.

### ⚠ Interpolation format and alignment clauses are honored or reported (baseline FR-051; #323)

- A format or alignment clause of an interpolation hole used to be dropped
  silently (`$"{n:D2}"` printed `3`, `$"{price:F2}"` the raw number, `{x,8}`
  no padding). TypeScript now calls one `metano-runtime` formatter per hole,
  picked by the value's type: `formatInteger(n, 32, "D2")`,
  `formatDouble(price, "F2")`, `formatInt64(l, "E2")`, and
  `alignValue(x, 8)` for an alignment alone. `ToString(format)` on `int`,
  the narrower integers and `double` calls the same formatter (it emitted
  `n.toString("D2")`, a `RangeError` at run time). The standard formats
  `G R F N E P C` (+ `D X` on integers) print what .NET prints in the
  invariant culture, any precision; a `double` rounds its exact value half to
  even (`2.675` with `F2` is `2.67`, `0.125` is `0.12`), as .NET Core 3.0+
  does. See [docs/bcl-mappings.md](docs/bcl-mappings.md#number-formatting-format-strings-and-interpolation-clauses).
- A clause the runtime does not format now reports `MS0001` (a warning) at the
  hole or the format argument and emits an expression that throws
  `Unsupported expression: UnsupportedFormat` where it is evaluated (in a
  static field initializer, when the module is imported): a custom numeric
  format (`0.00`, `#,##0`), the integers' `B`, and any other format on
  `decimal`, `float`, `Half`, a date or time, an enum, an `IFormattable` type,
  `object` or a type parameter. A `ToString(format)` whose format is not a
  constant calls the formatter on an integer or a `double`, which decides at
  run time: a `null` format is `G`, a custom format or `B` throws
  `NotSupportedException` naming the format, an invalid one `FormatException`.
- Formats that already printed .NET's text keep printing it: a format on a
  type that is not `IFormattable` (a `string`, `bool`, `char`, user class,
  record or struct) is ignored, as in C#; a `Guid`'s `D`, a numeric enum's `D`,
  a `[StringEnum]`'s `G` / `F`, a `DateOnly`'s `O` and `yyyy-MM-dd` and a
  `BigInteger`'s `G` / `R` / `D` print the clause-free text; a hole on a type whose `ToString(string)` has
  a declarative mapping lowers through it (`{g:N}` is `g.ToString("N")`).
- `ToString(format)` of any other BCL type used to be emitted as written,
  printing the wrong text (`12.5m.ToString("F2")` printed `12.5`) or throwing
  `RangeError` / `TypeError` (`BigInteger`, an enum, `nint`, `DateTime`,
  `v.ToString("D2", null)` with `T : IFormattable`) without a diagnostic. It now
  follows the hole's rule: a mapping or a default-text format lowers it, the
  rest reports `MS0001` at the format argument. `nint` / `nuint` call
  `formatInt64`; `Guid` gains `B` / `P` (and `b`, `p`, `n`). A user type's own
  `ToString(string)` is unchanged. `Guid`'s `X` still prints the hyphenated
  text through the fallback mapping (#657). Custom patterns stay reported, even
  where they equal the default text (`{b:#,##0}` on a `sbyte` / `byte`,
  `{d:yyyy'-'MM'-'dd}` on a `DateOnly`, `BigInteger`'s `D0` / `D1`).
  `value?.ToString(format)` and an empty or `null` format behave as the plain
  call with the type's default format. A static `ToString`
  (`Convert.ToString(s)`) is not a format call. The format-ignored rule reads
  the static type: a non-sealed class whose run-time value is `IFormattable`
  prints its default text.
- `long` / `ulong` gain `E`, `P` and `C` (they reported `MS0066` and threw),
  `R5` formats as `G5`, as in .NET, and a `long?` hole with a clause now
  formats instead of dropping the clause.
- A hole with a clause passes a nullable value to the formatter, which prints
  `null` as `""`, as the null-safe hole did. A hole without a clause, and
  `ToString()` without a format, emit exactly what they emitted before (the
  default conversion of `bool`, enums, `decimal` and `double` stays #308 /
  #662, and an alignment pads that text).
- Dart honors no clause yet: a hole with a clause (other than an ignored
  format), and every BCL `ToString(format)`, report `MS0001` and throw
  `UnsupportedError` instead of dropping the clause (or emitting
  `n.toString('D2')`).

### Generated code type-checks under `noUncheckedIndexedAccess` (baseline FR-069; #670)

- An array, `List<T>` or `string` element read is emitted as `values[i]!`, since C# types it `T`
  where the flag types the JavaScript read `T | undefined`; a nullable element is
  `(values[i] ?? null)`, because a `!` would strip its own `null`. A write target
  (`values[i] = v`, `??=`) stays bare and a compound assignment or `++` asserts it
  (`values[i]! += v`). In a `?.` chain the final `a?.[i]` is left unchecked (the chain already
  yields `null`). The output differs only by the `!` / `?? null`; run-time values are unchanged
  and an out-of-range read still gives `undefined` (C# throws; #651's FR-016 list keeps that
  gap). A `Dictionary` read (`d.get(k)`) is not covered.

### JSON: `JsonIgnoreCondition.WhenWritingNull` is mirrored (baseline FR-068; #667)

- A context with `DefaultIgnoreCondition = WhenWritingNull`, and a member with
  `[JsonIgnore(Condition = WhenWritingNull)]` (or `WhenWritingDefault` on a nullable member,
  whose default is `null`), leave a `null` member out of the written JSON, as System.Text.Json
  does; `[JsonIgnore(Condition = Never)]` keeps writing it under such a context. The property
  spec gains `omitWhenNull: true`; reading is unchanged. `MS0037` no longer reports these;
  `WhenWritingDefault` on a non-nullable member (a `0` or `false` default) and a context-wide
  `DefaultIgnoreCondition = WhenWritingDefault` still do.

### Dart `[StringEnum]` enums are valid Dart (baseline FR-070; #677)

- A `[StringEnum]` member keeps its C# name as the Dart identifier (a `[Name("in-progress")]`
  value used to become the identifier) and the enum declares `final String value;` and
  `const E(this.value);`.

### Class fields print without blank lines between them (baseline FR-084; #682)

- Formatting only, no behaviour change: in generated TypeScript, consecutive single-line
  class fields print one after another, with no blank line between them. A field that prints
  on more than one line (a block-form initializer, or a field with a doc comment) keeps a
  blank line above and below. One blank line still separates the fields from the
  constructor, accessors and methods. Every golden and generated sample changes by blank
  lines only. Dart output is unchanged.

### ⚠ `Metano.Build` becomes `Metano.Build.TypeScript` (baseline FR-085 to FR-087; #328)

- The MSBuild package is now one package per language target (ADR-0029).
  `Metano.Build.TypeScript` replaces `Metano.Build`, which gets no 3.x version
  and will be deprecated on nuget.org. Swap the `PackageReference`; every
  `Metano*` property keeps its name. A project that imports the targets by
  path imports `Metano.Build.TypeScript.targets` (next to
  `Metano.Build.Common.targets`).
- Each setting `Metano<S>` is the global value and `MetanoTs<S>` overrides it
  for the TypeScript target. With more than one target, prefer the prefixed
  form of `MetanoCompilerProject`, `MetanoExtraArgs` and
  `MetanoPostBuildCommand`: the global value reaches every enabled target.
  Two targets resolving to the same output directory fail with the new
  `METANOBUILD002` error.
- Settings are resolved when the build runs, so values set in
  `Directory.Build.targets` now apply (2.x ignored a `MetanoOutputDir` set
  there).
- `MetanoPostBuildCommand` no longer runs for a disabled target
  (`MetanoEnabled=false`); it still runs on every build of an enabled one,
  also when the transpile is up to date. `BeforeTargets` / `AfterTargets`
  hooks on `MetanoTranspile` keep their 2.x order: a `BeforeTargets` hook can
  still set `Metano*` properties or add `MetanoAsset` items.
- The toolchain key file is renamed
  `<project>.MetanoToolchain.typescript.cache`, so the first 3.0 build reruns
  the transpile once. A `Metano.Build.TypeScript` packaging smoke test now
  gates CI and the release.

### Toolchain

- The Roslyn packages (`Microsoft.CodeAnalysis.*`) move from 5.3.0 to 5.9.0.

### Import resolution, packaging and sample builds (#655, #675, #504, #356, #368, #678)

- A name the emitted code binds itself (a `catch` parameter, a chain's caught value, a
  `foreach` variable, a parameter, a local) no longer resolves to a module function another
  file exports under that name, so it no longer adds an import that can close an import cycle
  (`ReferenceError: Cannot access 'X' before initialization` under Node, `MS0005`).
- Every `using` alias a file spells now binds an import (`import { Widget as W1, Widget as W2 }`),
  also beside an `[ImportAlias]` pin of the same type; a `[NoContainer]` factory named like an
  aliased type is imported beside it; an aliased cross-package enum's synthesized default
  imports the enum; a type renamed with `[Name]` is called by its emitted name.
- **Templates name their receiver's type with `$Type`.** The text scan that imported a type a
  static `[MapMethod]` / `[MapProperty]` template named is gone: a user template such as
  `new Decimal(parseDecimalText($0))` must now be written `new $Type(parseDecimalText($0))`,
  and a user `[Emit]` that names its own class uses `$Type` too (`[Emit("$Type.make($0)")]`).
- A call that writes its type arguments to a transpiled generic method keeps them
  (`Guard.throwIfEqual<Shape>(circle, square)`); TypeScript no longer infers the type from the
  first operand only. A call that passes a `null` literal where a written type argument is not
  nullable in TypeScript (`Gen<string>(null)` in nullable-oblivious code) leaves the type to
  inference, as 2.x did.
- Two project types printed under one name that one file refers to (`Na.R23` and `Nb.R23`) now
  report `MS0070` at the second reference instead of silently binding one module.
- `package.json#sideEffects` lists the `[ModuleEntryPoint]` and `[StrictUnionGuard]` variant
  modules (their `dist` `.js` and source `.ts` paths) instead of a flat `false`, so bundlers
  keep a side-effect-only import of them; regeneration upgrades the earlier `false`. The
  compiled path follows the package's own `#/*` import target when the consumer retargeted it
  (`./lib/*` in a Vite app), and a hand-written entry that is not a string is kept as written.
- The `hello-world`, `sample-counter-v5` and `sample-queryable-sqlite` TypeScript builds are
  fixed, and CI builds every sample.
- **⚠ Target API:** `ITranspilerTarget.AdoptCachedOutput` takes the restored state as one
  `CachedTargetOutput` record (`Files`, `PackageDependencies`, `ModuleSideEffectFiles`):
  `AdoptCachedOutput(IrCompilation ir, Compilation? compilation, CachedTargetOutput cached)`.
  A custom target that overrides it reads `cached.Files` and `cached.PackageDependencies`
  instead of the 2.x `files` and `packageDependencies` parameters.

### ⚠ BCL members that diverged from JavaScript call runtime helpers (baseline FR-014, FR-053–FR-055; #661, #673, #668, #308, #465)

These members produced a wrong value, or none, in 2.x; each now calls a `metano-runtime`
function with .NET's semantics and exceptions, checked against real .NET (bun and Node) by
the rows of `tests/BclReference`.

- **`string` members (#661).** `s.Substring(i, n)` was `s.substring(i, n)`, which reads `n` as
  an end index (`"abcdef".Substring(1, 2)` printed `"b"`); it is `stringSubstring(s, i, n)` now,
  and so are `Remove`, `Insert`, `PadLeft` / `PadRight`, `ToCharArray(i, n)`, `IndexOf` /
  `LastIndexOf` with a start, count or `StringComparison`, `StartsWith` / `EndsWith` /
  `Contains` with a comparison, `Trim` / `TrimStart` / `TrimEnd` with characters, every `Split`
  with characters, a count or options, and `string.Join(sep, values, start, count)`. They throw
  `ArgumentOutOfRangeException` / `ArgumentNullException` for a bad argument where
  JavaScript clamps, swaps or ignores it. `StringComparison` and `StringSplitOptions` are
  `metano-runtime` enums now (they were unresolved identifiers). `Replace(a, b, comparison)` and
  `Replace(a, b, ignoreCase, culture)` pass the comparison to `stringReplace` (they were
  emitted as `s.replace(a, b, …)`, which ignores it and replaces the first match only).
- **`TimeSpan`, `DateTimeOffset`, `Guid`, `decimal` (#673).** `new TimeSpan(0, 5, 0)` was five
  *months*; the component constructors, `TimeSpan.Zero` and the single-argument `FromDays` …
  `FromMicroseconds` call `timeSpanFromParts` / `timeSpanZero` / `timeSpanFromX`.
  `new DateTimeOffset(…, offset)` is `dateTimeOffset(…)` (it was emitted by name).
  `Guid.Parse(s)` is `UUID.parse(s)`, which lowercases and checks the text, so two spellings
  of one GUID compare equal (it was `UUID.create(s)`, which kept the input's case).
  `decimal.TryParse(s, out d)` is `tryParseDecimal(s, Decimal)` (it reported MS0001).
- **`int.Parse` and friends (#668).** `int.Parse(s)` was emitted as `Int32.parse(s)`, an
  undeclared identifier; `Parse` / `TryParse` of `int`, `uint`, `short`, `ushort`, `byte` and
  `sbyte` (the `string`, provider and `NumberStyles` overloads) and `Parse` of `double`,
  `float` and `bool` call `parseInt32` … `parseBoolean`, which throw `FormatException` and
  `OverflowException` as .NET does. A constant `NumberStyles` outside the supported set reports
  `MS0066`, as for `long`.
- **Default text of `bool`, `double`, `float` (#308).** In interpolation, string concatenation,
  `ToString()` and `string.Join` over a value of those static types, `true` is `True`, `1e-7`
  is `1E-07`, `1e21` is `1E+21` and negative zero is `-0` (the invariant-culture text of
  .NET); the output calls `formatBoolean`, `formatDouble` and `formatSingle`. Output that
  printed a `double` or `bool` through JavaScript's conversion changes: regenerate, and use
  `[Emit]` or `[MapMethod]` where the JavaScript text is wanted. Other types and boxed values
  are untouched. A `decimal` cast to `double` or `float` reads its number (`m.toNumber()`,
  `Math.fround(m.toNumber())`), where 2.x kept the `Decimal` object, so `(double)m + 1`
  concatenated text; `formatDouble` / `formatSingle` throw a `TypeError` for a value that is
  not a `number` instead of printing `Infinity`. The `long?` operand of a `??` whose result
  is a `double` is converted before the fallback (`long? k = 4; double e = k ?? 1.5` prints
  `4`), and a `char` read as a `double` or `float` is its UTF-16 code (`double d = 'a'` is
  `97`; 2.x kept the `"a"` string).
- **`decimal` precision and rounding (#465, part a).** The class generated code imports for
  `decimal` is `Decimal` from `metano-runtime/system/decimal`: decimal.js configured with
  `System.Decimal`'s 29 significant digits and half-to-even rounding (decimal.js's own
  defaults are 20 digits and half up), so `decimal.MaxValue - 1` is exact. Generated imports
  change from `decimal.js` to `metano-runtime/system/decimal`; decimal.js is a dependency of
  `metano-runtime` now, and the compiler no longer adds it to a project's `package.json`.
  A project that declares its own `ExportFromBcl` for `decimal` keeps decimal.js. A quotient
  keeps 29 significant digits where .NET keeps 28 decimal places, and overflow past
  `decimal.MaxValue` does not throw (part b of #465 stays open).

### Calls, arguments and dispatch fixes (wave 3.0, group E1)

- A parameter of a transpiled struct type with the default `default` (`Point p = default`) is
  `p: Point = new Point()` in the callee, and a named call that skips it passes `new Point()`,
  where 2.x wrote `p: Point = undefined` and passed `null` (both fail `tsgo --strict`). Only a
  struct that declares no constructor qualifies; one with a declared or primary constructor keeps
  `undefined` (#676).
- An explicit reference downcast (`(Circle)shape`) types its TypeScript result: a class,
  record or mapped BCL exception target is checked by the new runtime helper `castTo` (`null`
  passes, another type throws `InvalidCastException`), any other target (an interface, a type
  parameter, `string`) is asserted (`o as IMarker`). 2.x emitted the bare operand, which failed
  to type-check at the first member read and let a wrong cast continue with `undefined`
  members. A cast that an earlier `x is T` already guards, and an upcast, keep the bare operand
  (FR-065, #669). A cast whose class import would close a cycle of value imports (inside a
  type the target derives from, or through a third module: `Node.describe` → `Printer.print`
  → `(Leaf)n` with `Leaf : Node`) is asserted (`n as Leaf`) instead of checked, since the
  import would fail when the modules load; such a cast does not throw `InvalidCastException`.
- A static constructor's body, dropped before with no diagnostic, lowers to a class
  `static { }` block that runs after the static fields (FR-066, #426); Dart and `[NoContainer]`
  classes report `MS0001`. A `static readonly` field or static get-only auto-property the static
  constructor writes is emitted without `readonly`, which TypeScript would reject in the block
  (TS2540).
- Overload dispatchers try a derived parameter type before its base (`F(ArgumentException)`
  before `F(Exception)`), and an `object` parameter accepts any value. A class declaring
  `Equals(T)` beside `Equals(object)` (or `ToString()` beside `ToString(string)`) emits one
  dispatched group, `equals` over `equalsOther` / `equalsObj`; a hand-written call to
  `x.equals(...)` that matched the object arm before keeps working, direct C# calls bind the
  fast path (FR-067, #677). `new BigInteger(x)` lowers to `BigInt(x)` instead of
  `new Object(x)`, and a reference to a local function reports `MS0001` instead of printing
  `C.local(...)`.

### ⚠ Defaults, record equality and null guards (baseline FR-059, FR-060, FR-061)

- A target-typed `default` (a local initializer, a conditional arm, a `??` operand, a
  collection element, `default(T)`) lowers to the default of its converted type (`false`,
  `0`, `0n`, the zero enum member) where 2.x printed `undefined` (#627, #372, #612). A struct
  that declares no constructor is `new Point()` there too, as in a `= default` parameter and an
  `OrDefault` / `Find` / `GetValueOrDefault` miss (`new Box<number>()` keeps its type
  arguments); when a field of that struct has no default of its own (a type parameter, a
  tuple, a `DateTime`), the `default` literal still reports `MS0001`, since the field stays
  undefined. A field of type `char`, `Guid` or `BigInteger` without an initializer is
  initialized to `"\u0000"`, `UUID.empty` or `0n` (2.x left it undefined). A type no
  expression spells (a struct that declares a constructor, a tuple, a `[StringEnum]` without a
  zero member) reports `MS0001`. An enum field's implicit initializer is the member whose value is `0`
  (`0 as E` when there is none), not the lowest member; a `[StringEnum]` field without a zero
  member has no initializer (#409).
- The generated record `equals` compares `double` / `float` fields through `valueEquals` (`NaN`
  equals itself). A field whose static type does not say how it compares (a type parameter,
  `object`, an interface, a class) goes through the new runtime `defaultEquals`, .NET's
  `EqualityComparer<T>.Default`: the value's own `equals` (records, `Decimal`, user overrides),
  a `TimeSpan` by length, `NaN` equal to itself, else reference equality, so `Box<TimeSpan>`
  and `Box<decimal>` compare by value and `Box<SomeClass>` / `Box<int[]>` by reference as on
  .NET. A plain (non-record) struct now gets a generated `equals` / `hashCode` over its fields
  (.NET's `ValueType.Equals`) unless it declares its own, so a struct behind an `object` or
  interface field compares by value. A struct's own `Equals(S)` keeps replacing it when it
  implements `IEquatable<S>` (or a transpiled interface's `Equals(S)`), as
  `EqualityComparer<S>.Default` uses it on .NET, so a boxed comparison of that struct also
  reaches it where .NET compares fields; an `Equals(S)` that implements nothing no longer
  replaces the field comparison and reports `MS0063`. A struct whose `Equals(S)` replaces it only
  through a transpiled interface (no `IEquatable<S>`, no `override Equals(object)`) also reports
  `MS0063`: sets, dictionaries and record fields compare through it where .NET compares fields.
  Two methods spelled `equals` by `[Name]` report `MS0001`, and so does any replacing
  declaration spelled `equals` beside a `[Name("equals")]` method (a struct's explicit or
  implicit `IEquatable<S>.Equals`, a record's own `Equals(R)`), or a replacing `Equals` renamed by
  its own `[Name]` beside one. A struct's explicit
  `IEquatable<T>.Equals` with `T` other than the struct is now emitted as a checked overload of
  its `equals` (`equalsO` behind a runtime check); two explicit `IEquatable<T>.Equals` that claim
  one name are both dropped with an `MS0001` warning, as is one whose name a public method with
  the same parameters holds; record structs included. A kept explicit `Equals(S)` beside another
  `Equals` overload also answers `a.Equals(b)` and `object.Equals(a, b)`, where .NET compares
  the fields. Known limitation, as in 2.x: a boxed
  comparison of a struct with only `Equals(S)` against `null` or a foreign value reaches its body
  unguarded. A user method spelled like a
  synthesized member (`Equals(T other, double tol)`, `HashCode(int seed)`, a record's
  `With(int a)`, or a lone method whose `[Name]` is `equals` / `hashCode` / `with`) now shares
  the name: the synthesized member joins its overload group, the
  dispatcher keeps `equals` / `hashCode` / `with`, the synthesized member's fast path is
  `equalsByValue` / `hashCodeByValue` / `withByValue`, and C# calls bind statically to the fast
  paths (spec 013 FR-038–FR-045). An overload a dispatcher cannot tell from the synthesized
  member reports `MS0063`; one that implements an interface member reports `MS0001` as an
  error, as does a field or property emitted as `equals`. A derived record's output changes when
  its base starts to join, so consumers of a package must re-transpile. Known limitation: a
  foreign argument a user overload accepts takes that overload through the dispatcher
  (`object.Equals(a, 1.0)` with a user `Equals(double)`), where .NET calls `Equals(object)`. An array in a `T`, `object` or
  interface position compares element by element, so a tuple there compares by value as on
  .NET; a C# array there also compares element-wise where .NET compares the reference, as in
  2.x (the run time cannot tell a tuple from an array). Fields of a known class type, which 2.x compared structurally,
  now compare by reference unless the class declares `Equals` (#674). The `ThrowIfEqual` /
  `ThrowIfNotEqual` guards now admit sealed generic records and records with a `double` /
  `float` field.
- The null guard of a string concatenation, an interpolation hole, a list pattern's subject
  and an exception's `message` follows the declared type of the operand, not the optimistic
  flow state: `"a" + s! + "b"` and a field checked and then cleared by a call keep
  `s ?? ""`, and nullable-disabled code guards every reference operand, so its output gains
  `?? ""` where 2.x printed `null` (#649). Known limitation: an `[Emit]` template that
  produces a never-nullish expression (`typeof $0`) and returns a `string` in nullable-disabled
  code gets the guard, which tsgo rejects as TS2869.

### ⚠ User-defined operators and conversions run at their use sites (baseline FR-077, FR-079; #599)

- Every application of a user-defined operator calls its type's static helper: `a + b` is `Money.__add(a, b)`, `-a` is `Money.__negate(a)`, a dispatched group calls the fast path C# resolved (`Vec.__multiplyDoubleVec(2, v)`, a branded type its companion function `Ticks.__addTicksInt64`), and a type read only from metadata calls the dispatcher. They printed the JavaScript operator on the objects (`a + b` concatenated, `-a` gave `NaN`).
- `x += y` stores `x = Money.__add(x, y)` and `x++` stores `x = Money.__increment(x)`, evaluating the target once; they called the instance helper (`x = x.$add(y)`) or printed `x++` on the object.
- Every user-defined conversion runs: `Money m = 7` is `Money.__implicitFromInt32(7)`, `(long)m` is `Money.__toInt64(m)`, `decimal d = m` calls `Money.__implicitToDecimal(m)`, a `foreach (Money m in ints)` variable converts each element, and a condition over `implicit operator bool` calls it. They passed the operand unchanged.
- Lifted operators and conversions over `Nullable<T>` yield `null` (or `false` for a relational operator); a lifted `==` / `!=` compares `null` as C# does. A test against a constant `null` (`x == null`) stays a plain null check.
- A user `true` / `false` operator in a condition or `&&` / `||`, a C# 14 instance compound or increment operator and an operator of a `[PlainObject]` type report `MS0001` in TypeScript.
- Dart output does not change.

### ⚠ Operator helpers and conversion statics are renamed (baseline FR-078; #600, #599)

- Each conversion operator is its own static, named by its kind, direction and converted type,
  with no dispatcher: `__toInt32` / `__fromInt32` for an explicit conversion,
  `__implicitToInt32` / `__implicitFromInt32` for an implicit one, `__checkedToInt32` for a
  `checked` one (`implicit operator Money(int c)` → `Money.__implicitFromInt32`,
  `explicit operator long(Money m)` → `Money.__toInt64`). A conversion without `[Name]` used to
  emit nothing; a `[Name("toInt")]` names that one conversion (`__toInt`), not its group, and
  the old `__toInt` dispatcher and `$toInt` instance helper are gone. Switching a conversion
  between implicit and explicit renames its static.
- Unary `-` and `+` are `__negate` / `__plus` (and `$negate()` / `$plus()`), no longer
  `__subtract` / `__add`; `++`, `--`, `>>>` and `checked` operators get `__increment`,
  `__decrement`, `__shiftRightUnsigned` and `__checkedAdd`-style helpers.
- An operator declared for its right operand (`operator +(int a, Meters m)`) no longer has an
  instance form (`$add`), which passed `this` as the left operand; an operator group's instance
  dispatcher keeps only the overloads whose left operand is the type.
- A `[Branded]` / `[InlineWrapper]` companion uses the same names (`Qty.__add`,
  `Qty.__implicitFromInt32`, and exported per-overload fast paths such as
  `Ticks.__addTicksInt64`) instead of `op_Addition` / `op_Implicit`. Every conversion is
  emitted, the identity unwrap to the underlying primitive included
  (`Slug.__implicitToString(s)` returns `s`), so a package that sees the type only through
  metadata names the same function.
- A conversion spells a converted type by its CLR name, as method fast paths do
  (`[Name("Cash")] class Money` → `__implicitFromMoney(m: Cash)`).
- Every conversion is declared, so one to a type the target neither emits nor maps
  (`implicit operator System.Text.StringBuilder(Vec v)`) now emits
  `__implicitToStringBuilder(v: Vec): StringBuilder` and fails `tsc` with `TS2304`, where nothing
  was emitted before; put `[Ignore]` on that conversion.
- A derived class that redeclares an operator or a conversion of its emitted base declares its
  static (and instance form) `override` (`static override __toInt32`), which fixes `TS4114`.
- The statics of a generic type re-declare its type parameters (`static __add<T>(...)`),
  which fixes `TS2302`.

## 2.10.1

_2026-11-03_

### 🐛 Bug Fixes

* apply review notes to the compiler-version cache key ([#433](https://github.com/danfma/metano/issues/433)) ([33d4dc8](https://github.com/danfma/metano/commit/33d4dc8e26bb8c0a2e4c16ff4ba2e7d09f89e9d6))
* regenerate output when the Metano version changes ([#433](https://github.com/danfma/metano/issues/433)) ([dac736e](https://github.com/danfma/metano/commit/dac736e0efb2f31022f5b398450ea418766768c4))

## 2.10.0

_2026-11-03_

### ✨ Features

* dispatch polymorphic JSON in the runtime ([#268](https://github.com/danfma/metano/issues/268)) ([c07d251](https://github.com/danfma/metano/commit/c07d251795ea28b90f03d46f402e27b047f739de))

### 🐛 Bug Fixes

* align misplaced JSON metadata errors with STJ ([#268](https://github.com/danfma/metano/issues/268)) ([30cfa36](https://github.com/danfma/metano/commit/30cfa360575b2e866cebd7c902c09fd8079dc060))
* apply review notes to the JSON wire order and limits ([#268](https://github.com/danfma/metano/issues/268)) ([7faed2a](https://github.com/danfma/metano/commit/7faed2a2b841ccef424f39769d8a6a41a87bc6d7))
* keep a pending $values misplaced on an ignore-unrecognized abstract base ([#268](https://github.com/danfma/metano/issues/268)) ([74b692c](https://github.com/danfma/metano/commit/74b692cf139d439ed1ba50db0d0b4c36f87a0b71))
* match STJ for out-of-order $values metadata ([#268](https://github.com/danfma/metano/issues/268)) ([00a8541](https://github.com/danfma/metano/commit/00a8541d16bdaf6ae687a6f9e958b672901aafa3))
* reject an out-of-order tag on a concrete polymorphic base ([#268](https://github.com/danfma/metano/issues/268)) ([24f5500](https://github.com/danfma/metano/commit/24f5500357b0745e5ad4a4b2900dea5545e20fb5))
* write JSON derived-first and report runtime errors as JsonException ([#268](https://github.com/danfma/metano/issues/268)) ([729984d](https://github.com/danfma/metano/commit/729984d8f7ddcec7f6a8605e83a9406404673c67)), closes [#425](https://github.com/danfma/metano/issues/425)

### 📝 Documentation

* note the source condition requirement and NuGet registration delay ([9417adb](https://github.com/danfma/metano/commit/9417adb560befd040996ccf0bd617802e87adfeb))

## 2.9.0

_2026-11-03_

### ✨ Features

* let generated packages name their source export condition ([#424](https://github.com/danfma/metano/issues/424)) ([6236f8e](https://github.com/danfma/metano/commit/6236f8e930891f6d74a9df27b00fcd592d15a801))

### 🐛 Bug Fixes

* admit library bases and closed generic types in the SerializerContext closure ([#268](https://github.com/danfma/metano/issues/268)) ([a66fbb5](https://github.com/danfma/metano/commit/a66fbb5894c6faa7d40057d36e9855d42827ceb4))
* dedupe only redeclared JSON properties and reject unreachable generic arguments ([#268](https://github.com/danfma/metano/issues/268)) ([67d16d2](https://github.com/danfma/metano/commit/67d16d21b6e682ccfc040322912d0c1188ad224d))
* drop a stale source condition only on a rename ([#424](https://github.com/danfma/metano/issues/424)) ([9ab8fe5](https://github.com/danfma/metano/commit/9ab8fe50eb0066f8a3be493507c796c818aa5514))
* hide a base JSON property only under an ignored override ([#268](https://github.com/danfma/metano/issues/268)) ([1d6a2bb](https://github.com/danfma/metano/commit/1d6a2bbfaa9f7f4e92ffa6a44f0a2827033688f7))
* hoist module-local nested types and keep their names TypeScript-only ([#412](https://github.com/danfma/metano/issues/412)) ([c5c8478](https://github.com/danfma/metano/commit/c5c84780c5f3dbaaf74cc0737a0bc94d9163554c))
* keep non-public nested types out of the companion exports ([#412](https://github.com/danfma/metano/issues/412)) ([7a33475](https://github.com/danfma/metano/commit/7a33475843cecb31d577dcfd6cccede76a59f01a))
* name module-local guards after their declared name ([#412](https://github.com/danfma/metano/issues/412)) ([1b3c59d](https://github.com/danfma/metano/commit/1b3c59d484a16d44832bc761a55cdc974b0e7998))
* place module-local nested types by their load-time dependencies ([#412](https://github.com/danfma/metano/issues/412)) ([73607dd](https://github.com/danfma/metano/commit/73607dd43176401a8d417d896aa2a0ac20df606c))

### 📝 Documentation

* state exactly which source condition a rename replaces ([#424](https://github.com/danfma/metano/issues/424)) ([bfde7a2](https://github.com/danfma/metano/commit/bfde7a2f48aa69b564c877edd10c2f7b1e464dc8))

## 2.8.0

_2026-11-03_

### ✨ Features

* deserialize branded values through their [JsonFactory] ([#269](https://github.com/danfma/metano/issues/269)) ([0069938](https://github.com/danfma/metano/commit/0069938dbf5baaf6c0f5ba4a586af3ab8fb99eb3))
* emit value roots in the generated SerializerContext ([#270](https://github.com/danfma/metano/issues/270)) ([a6485d8](https://github.com/danfma/metano/commit/a6485d838e504419c33ac6c01bf989f547842600))

### 🐛 Bug Fixes

* address review findings on the operator lowering ([#323](https://github.com/danfma/metano/issues/323)) ([63c6092](https://github.com/danfma/metano/commit/63c6092b4707c29d7114e546fbc5ac90e3ff10da))
* apply review notes to the SerializerContext value roots ([#270](https://github.com/danfma/metano/issues/270)) ([5154e97](https://github.com/danfma/metano/commit/5154e97894cdbfd2e1fcfbb6976cf92937c562dd))
* clarify the body-export diagnostic and pin captured field naming ([#390](https://github.com/danfma/metano/issues/390)) ([2da05e1](https://github.com/danfma/metano/commit/2da05e196d848e23aa914c8ac953c68ddb591f2d))
* close switch-statement and is-designator scoping gaps ([#317](https://github.com/danfma/metano/issues/317)) ([de60f35](https://github.com/danfma/metano/commit/de60f35f08102cc1c0d263bacae1e2679101e8cd))
* compare nested and nullable tuples by value ([#331](https://github.com/danfma/metano/issues/331)) ([08c42f0](https://github.com/danfma/metano/commit/08c42f0ba03867c87755a63909af415c5ee0f787))
* confine tuple equality lowering to tuple receivers ([#331](https://github.com/danfma/metano/issues/331)) ([0752195](https://github.com/danfma/metano/commit/07521950132215c12ea0adaf8f6f81d9da0c8d2e))
* decide per-class storage from the property alone and lower compound writes ([#341](https://github.com/danfma/metano/issues/341)) ([9dfe8ad](https://github.com/danfma/metano/commit/9dfe8ad976d80306e773de01e04bb8c791271b8a)), closes [#363](https://github.com/danfma/metano/issues/363)
* declare metano-runtime as a peer dependency of generated packages ([#411](https://github.com/danfma/metano/issues/411)) ([b1adb3f](https://github.com/danfma/metano/commit/b1adb3f7244b5221856b9b5cd51d8499e93dc29d))
* detect library auto-properties from compiled accessors and cover hiding properties ([#341](https://github.com/danfma/metano/issues/341)) ([01bfcd4](https://github.com/danfma/metano/commit/01bfcd424ee23b914ad58ce3d07454293e0c648d)), closes [#363](https://github.com/danfma/metano/issues/363)
* fold tuple equality only for arguments that cannot hold the tuple ([#331](https://github.com/danfma/metano/issues/331)) ([a582f88](https://github.com/danfma/metano/commit/a582f88fa6bf3b330a4bbff57dac4b8d97712ca0))
* forward inherited accessors across override chains and respect an ancestor ToJSON ([#341](https://github.com/danfma/metano/issues/341)) ([f1a1c62](https://github.com/danfma/metano/commit/f1a1c6226374d32a1381c4920b38eff510405b12)), closes [#363](https://github.com/danfma/metano/issues/363)
* give overridden auto-properties per-class backing fields ([#341](https://github.com/danfma/metano/issues/341)) ([0fb3913](https://github.com/danfma/metano/commit/0fb39137f68cd3133d4d322a83609bb506612852)), closes [#363](https://github.com/danfma/metano/issues/363)
* ignore non-emitted siblings when checking the factory's TS name ([#269](https://github.com/danfma/metano/issues/269)) ([28786e0](https://github.com/danfma/metano/commit/28786e0c9d51fa959cc32634f7d8e7f1989b263b))
* ignore skipped ToJSON methods when deciding toJSON synthesis ([#341](https://github.com/danfma/metano/issues/341)) ([368c495](https://github.com/danfma/metano/commit/368c49556b5be0a83871702210fba61b5c757984)), closes [#363](https://github.com/danfma/metano/issues/363)
* keep ObjectArgs keys and null-safe decimal conversion in the operator lowering ([#323](https://github.com/danfma/metano/issues/323)) ([e0710c8](https://github.com/danfma/metano/commit/e0710c870a42589838b0cd93277caeef6aa1152a))
* keep tuple equality for convertible and generic arguments ([#331](https://github.com/danfma/metano/issues/331)) ([221fd5f](https://github.com/danfma/metano/commit/221fd5f2b5ba744c9e5fac39a3d706f36bf736ca))
* limit the computed-read capture to is expressions and type hoisted locals ([#317](https://github.com/danfma/metano/issues/317)) ([af8bce6](https://github.com/danfma/metano/commit/af8bce674e184da68160c8555bfce90e7ee86c7d))
* lower boolean logic, nullable increments and related operators ([#323](https://github.com/danfma/metano/issues/323)) ([23b4a7c](https://github.com/danfma/metano/commit/23b4a7c8cb0683250e4fa0f0f1a15ef6993ac1cf))
* name parameters and module variable reads through the same policy ([#390](https://github.com/danfma/metano/issues/390)) ([e1f7ba4](https://github.com/danfma/metano/commit/e1f7ba4dcb5316ee0630173338eb6f3809de6a7f))
* read hash-set JSON as an array like the array converter ([#270](https://github.com/danfma/metano/issues/270)) ([5a7570e](https://github.com/danfma/metano/commit/5a7570eae8acd6bd41accb25eee164ebe070404a))
* refresh the generated runtime peer and pin the release version ([#411](https://github.com/danfma/metano/issues/411)) ([fdffc21](https://github.com/danfma/metano/commit/fdffc21e8b4f47442af708edd95703d7067826d1))
* regenerate every sample in the local targets gate ([4d3c5fd](https://github.com/danfma/metano/commit/4d3c5fd426bd8ac3fb251df83b8e2812582b1208))
* rename locals whose emitted names collide ([#398](https://github.com/danfma/metano/issues/398)) ([b49c46e](https://github.com/danfma/metano/commit/b49c46e2691c4dec197de5bc85292a4ce1d716e4))
* require public package factories and apply review notes ([#269](https://github.com/danfma/metano/issues/269)) ([f9feb93](https://github.com/danfma/metano/commit/f9feb93c59a570465feeac9bfbe429b3e0d1dccd))
* route every local binding through one naming decision ([#390](https://github.com/danfma/metano/issues/390)) ([d51f613](https://github.com/danfma/metano/commit/d51f613232872ec9ab2908e7c91e84fe6296104b))
* store moved initializers to the backing field and forward inherited accessors ([#341](https://github.com/danfma/metano/issues/341)) ([127aeee](https://github.com/danfma/metano/commit/127aeee19e1d71ef185a10aa97e174b3d364c775)), closes [#363](https://github.com/danfma/metano/issues/363)
* thread the bridge context through boolean logic lowering ([#323](https://github.com/danfma/metano/issues/323)) ([8a5e728](https://github.com/danfma/metano/commit/8a5e728dfa7314ccbb7bf10f92866b7efdb9fa81))
* use one emitted name for const locals and their reads ([#390](https://github.com/danfma/metano/issues/390)) ([e44ad16](https://github.com/danfma/metano/commit/e44ad1683e4eafdf7c8f49e9ca43a0cc368dec6e))

### ♻️ Refactor

* apply review feedback to the designator scoping fix ([#317](https://github.com/danfma/metano/issues/317)) ([e8c61dd](https://github.com/danfma/metano/commit/e8c61dde73c32d26eacadd2aeff5fe091c732895))
* apply review feedback to the operator lowering ([#323](https://github.com/danfma/metano/issues/323)) ([bd33234](https://github.com/danfma/metano/commit/bd33234b8aa9f3e4ecc4d8a9478616f6dbcd2c8c))
* apply review notes to the capture-scan tidy ([#332](https://github.com/danfma/metano/issues/332)) ([0b6a167](https://github.com/danfma/metano/commit/0b6a16735878aa62f84c66e28bdc595e71963b20))
* keep tuple-literal purity local to the tuple equality fold ([#331](https://github.com/danfma/metano/issues/331)) ([074d980](https://github.com/danfma/metano/commit/074d98072b1243a00ff118a8ccf0bc988f69bc28))
* lower tuple value equality through a dedicated IR node ([#331](https://github.com/danfma/metano/issues/331)) ([54bd246](https://github.com/danfma/metano/commit/54bd2468f1fb4c4fbade744754f2d97d7ef4136d))
* order the runtime peer helpers and drop an unused fallback ([#411](https://github.com/danfma/metano/issues/411)) ([d5a5088](https://github.com/danfma/metano/commit/d5a508804496f035369228485389cc4e9f28ee24))
* pin parameter naming at every binding site ([#390](https://github.com/danfma/metano/issues/390)) ([261688d](https://github.com/danfma/metano/commit/261688d583d9fc1342a297a5a77fa961b4bfdbf6))
* share the subject capture and finish the lowering rename ([#317](https://github.com/danfma/metano/issues/317)) ([d727f0d](https://github.com/danfma/metano/commit/d727f0d2ae0740090fe9a4d6771ccdb9940cd263))
* thread one bridge context through the TS lowering ([#388](https://github.com/danfma/metano/issues/388)) ([03aa053](https://github.com/danfma/metano/commit/03aa05377ea0e6a3a8e23024abd33610e230f69a))
* tidy the per-class backing field implementation ([#388](https://github.com/danfma/metano/issues/388)) ([56e4934](https://github.com/danfma/metano/commit/56e49348c4710101143984fb5b263682f10a9895)), closes [#383](https://github.com/danfma/metano/issues/383)
* tidy the primary-constructor capture scan and converge the write-target checks ([#332](https://github.com/danfma/metano/issues/332)) ([6eced8c](https://github.com/danfma/metano/commit/6eced8c33142ceacfc709af48b1335df6b884943))

### 📝 Documentation

* specify value-to-string formatting parity for bool and floating point ([#308](https://github.com/danfma/metano/issues/308)) ([7512957](https://github.com/danfma/metano/commit/7512957d2baa722f40389174533df8042dbca030)), closes [#323](https://github.com/danfma/metano/issues/323)

## 2.7.2

_2026-11-02_

### 🐛 Bug Fixes

* assign positional overrides and emit abstract properties without storage ([#336](https://github.com/danfma/metano/issues/336)) ([b6b363d](https://github.com/danfma/metano/commit/b6b363d9bbd274db3265edcafa26f51fdd4e70b6)), closes [#324](https://github.com/danfma/metano/issues/324)
* bound package root discovery to the workspace ([#334](https://github.com/danfma/metano/issues/334)) ([5609a99](https://github.com/danfma/metano/commit/5609a99fab69f96a1b04a23c78a821757acfd189))
* call the shared Dart transpile helper in the merged Dart tests ([ae36abf](https://github.com/danfma/metano/commit/ae36abf1c174f6de8e2337a7d605a620ff5632d2)), closes [#336](https://github.com/danfma/metano/issues/336) [#324](https://github.com/danfma/metano/issues/324) [#337](https://github.com/danfma/metano/issues/337)
* cast null initializers of nullable locals to the declared type ([#364](https://github.com/danfma/metano/issues/364)) ([c916474](https://github.com/danfma/metano/commit/c916474d4a05f937c843b5b2a0362541ad8abe77))
* cover field initializers and constructor defaults in Dart body imports ([#367](https://github.com/danfma/metano/issues/367)) ([1e11fb0](https://github.com/danfma/metano/commit/1e11fb004f3a202f9ba9adbe46763c35709ab32f))
* drop the explicit Invoke on delegates with a [This] parameter ([#362](https://github.com/danfma/metano/issues/362)) ([c1494ea](https://github.com/danfma/metano/commit/c1494ea0e62b2ff2ea47ae967d51a8d148e227b6))
* drop the MSBuild stamp when the package root is refused ([#334](https://github.com/danfma/metano/issues/334)) ([cbda4e7](https://github.com/danfma/metano/commit/cbda4e7a0170dc3b2b7d5a07e4099f67df7ea30e))
* emit nullable params parameters as array-typed rest parameters ([#329](https://github.com/danfma/metano/issues/329)) ([b2411bd](https://github.com/danfma/metano/commit/b2411bda575151e81d580a2ac77c6fe4490f73a1)), closes [#320](https://github.com/danfma/metano/issues/320)
* import extension libraries called on delegates in Dart ([#367](https://github.com/danfma/metano/issues/367)) ([809b4b2](https://github.com/danfma/metano/commit/809b4b22b781a34672e04320c7e576cd64d0b956))
* keep the declared type of nullable locals initialized to null ([#364](https://github.com/danfma/metano/issues/364)) ([1dc6f8a](https://github.com/danfma/metano/commit/1dc6f8ae5ca630f1ca61595fd206025e62323c91))
* keep the member access when invoking a delegate-typed member ([#337](https://github.com/danfma/metano/issues/337)) ([de2f55c](https://github.com/danfma/metano/commit/de2f55c30426b6cce0e3a5dcd390e66e19986fd5))
* lower extended case sub-patterns and string BCL members correctly ([#324](https://github.com/danfma/metano/issues/324)) ([8959d18](https://github.com/danfma/metano/commit/8959d1859e1fdf53b92e7e6c25b7ed5ae7203a1c))
* match overridden properties by their generic definition ([#366](https://github.com/danfma/metano/issues/366)) ([324a0df](https://github.com/danfma/metano/commit/324a0dfc85f4b72b3257b7892b1c1140fc7288cc))
* never take the directory shared with the project as package root ([#334](https://github.com/danfma/metano/issues/334)) ([59ed4f8](https://github.com/danfma/metano/commit/59ed4f8d639982ec217bc063a21732c0ea59ee19))
* point the MS0044 message at the documented rules ([#334](https://github.com/danfma/metano/issues/334)) ([f218ff4](https://github.com/danfma/metano/commit/f218ff47af5fa6d779bc56859c8381b6a3a4fcea))
* prefer repository markers and fold the refusal into the reconciler ([#334](https://github.com/danfma/metano/issues/334)) ([04d2e2f](https://github.com/danfma/metano/commit/04d2e2fb495d0f89aa01a5721adcea28669bc026))
* refuse an output unrelated to the project without a package.json ([#334](https://github.com/danfma/metano/issues/334)) ([7b36d5b](https://github.com/danfma/metano/commit/7b36d5b13b500eeb3a78831629cc094946509376))
* report constructors that read overridable properties ([#366](https://github.com/danfma/metano/issues/366)) ([7a29820](https://github.com/danfma/metano/commit/7a29820fe40e60886c3d10135d0ea1e3135e6b26))
* spread params arrays forwarded through constructor initializers ([#365](https://github.com/danfma/metano/issues/365)) ([ad6f367](https://github.com/danfma/metano/commit/ad6f3674c9583b601f7500691322ac1b8683115f))

### ♻️ Refactor

* address review notes on the [#324](https://github.com/danfma/metano/issues/324) fix ([#324](https://github.com/danfma/metano/issues/324)) ([2da0758](https://github.com/danfma/metano/commit/2da0758fce8878864b0e2f609c83c0b3a495a0f6))
* keep the iteration bridge free of Roslyn diagnostics ([#329](https://github.com/danfma/metano/issues/329)) ([9ad9bab](https://github.com/danfma/metano/commit/9ad9bab3c253cfda683c6e6e2b33a7da31cb69da))
* share the droppable delegate-invoke predicate ([#337](https://github.com/danfma/metano/issues/337)) ([b37a310](https://github.com/danfma/metano/commit/b37a310464b9f99af2ef540b01ea62ed55346b6e))
* simplify the unmapped-static report and export keys ([#324](https://github.com/danfma/metano/issues/324)) ([438e83b](https://github.com/danfma/metano/commit/438e83bbeec6e9d09039dc7fe1b8faef5ec6e5f2))
* tidy the hiding check and its tests after review ([#336](https://github.com/danfma/metano/issues/336)) ([4d6d4e4](https://github.com/danfma/metano/commit/4d6d4e4f986640f9c460d465569fe09e4124e076))
* tighten the [#324](https://github.com/danfma/metano/issues/324) fix after review ([#324](https://github.com/danfma/metano/issues/324)) ([20c7516](https://github.com/danfma/metano/commit/20c75167f5680272e342bea34cee7890cab78cfd))

### 📝 Documentation

* add specification for runtime collections and type descriptors ([#335](https://github.com/danfma/metano/issues/335)) ([0f6a860](https://github.com/danfma/metano/commit/0f6a8601ba6320be51a6f37d0d6b2a21dc90baf4))

## 2.7.1

_2026-11-01_

### 🐛 Bug Fixes

* decide primary-ctor promotion once per type and never promote a written parameter ([#254](https://github.com/danfma/metano/issues/254)) ([f60a70b](https://github.com/danfma/metano/commit/f60a70b51617a4aa00aa3ee3938e65df2cbe714c))
* emit static properties and containerless module fields ([#314](https://github.com/danfma/metano/issues/314)) ([a1e9314](https://github.com/danfma/metano/commit/a1e931461998a9dd699f939db5321f9923337c1c))
* give written and divergent primary-ctor parameters their own mutable storage ([#254](https://github.com/danfma/metano/issues/254)) ([d9efbcd](https://github.com/danfma/metano/commit/d9efbcde0c43c9c40c4d3e262f7c09a94934f2d6))
* iterate IEnumerable implementations and transpile custom init accessors ([#320](https://github.com/danfma/metano/issues/320)) ([b6e33ed](https://github.com/danfma/metano/commit/b6e33edcc00de221cc83ea5e3cf871940d2ec536))
* keep a written positional record parameter promoted ([#254](https://github.com/danfma/metano/issues/254)) ([ac91e47](https://github.com/danfma/metano/commit/ac91e47f36b2d3a5a3815b65e4e41cd61387ca7a))
* keep C# reference equality and tuple semantics in equality lowering ([#254](https://github.com/danfma/metano/issues/254)) ([a72dfb1](https://github.com/danfma/metano/commit/a72dfb1d2242c97a64ee4bc3bd1c9fc67e171f07))
* keep tuple and promoted-parameter lowering sound against divergent operands ([#254](https://github.com/danfma/metano/issues/254)) ([3547daa](https://github.com/danfma/metano/commit/3547daa9f7fb79b7dc22f651e0e67cbbafe30c25))
* lower record and tuple value equality, hash codes and primary-ctor initializers ([#254](https://github.com/danfma/metano/issues/254)) ([335a6c6](https://github.com/danfma/metano/commit/335a6c629eb6ab93183ef6becd9dde8006bc52bd)), closes [#314](https://github.com/danfma/metano/issues/314)
* read promoted primary-ctor parameters and compare tuples in object.Equals ([#254](https://github.com/danfma/metano/issues/254)) ([45f1f5d](https://github.com/danfma/metano/commit/45f1f5d0d2150b455527f29ebe09f5c733b6cd3e))
* reject module-variable uses that ES modules cannot keep ([#314](https://github.com/danfma/metano/issues/314)) ([23ce3a9](https://github.com/danfma/metano/commit/23ce3a94dad9d011908eedbc138e4885aab3b792))
* resolve enumeration contracts through Roslyn and bridge hand-written enumerators ([#320](https://github.com/danfma/metano/issues/320)) ([b952cb7](https://github.com/danfma/metano/commit/b952cb72bc0c7af12b938d574207350ef3df2aaf))
* stop declaring an enumerator with its own next as an Iterator ([#320](https://github.com/danfma/metano/issues/320)) ([3996e67](https://github.com/danfma/metano/commit/3996e67bd341aca5708dd9f88ff4c6888eed1525))
* throw on duplicate Dictionary.Add keys and initialize required and struct members ([#320](https://github.com/danfma/metano/issues/320)) ([4ac3859](https://github.com/danfma/metano/commit/4ac38590568e489e2d8e0e5200858964097bce5b)), closes [#291](https://github.com/danfma/metano/issues/291)

### ♻️ Refactor

* name the record promotion tests after their behavior ([#254](https://github.com/danfma/metano/issues/254)) ([e14e7e1](https://github.com/danfma/metano/commit/e14e7e16d5b2059e6fe01d20afba733922e040e0)), closes [#332](https://github.com/danfma/metano/issues/332)
* share the promotion candidate lookup and tighten the promotion tests ([#254](https://github.com/danfma/metano/issues/254)) ([1fc1763](https://github.com/danfma/metano/commit/1fc176329d57f61613834e41ac42786bebcbb597)), closes [#331](https://github.com/danfma/metano/issues/331)
* tidy the promoted-parameter read and the tuple object.Equals entry ([#254](https://github.com/danfma/metano/issues/254)) ([c8da097](https://github.com/danfma/metano/commit/c8da0973410d031e97fd43525a39d51b3f547634))

### 📝 Documentation

* apply maintainer decisions to the C# unions specification ([#304](https://github.com/danfma/metano/issues/304)) ([5012eb5](https://github.com/danfma/metano/commit/5012eb579ee9b38f1e2041d3df23e46ef4f17906)), closes [#298](https://github.com/danfma/metano/issues/298) [#239](https://github.com/danfma/metano/issues/239)
* apply maintainer decisions to the polymorphic json specification ([#268](https://github.com/danfma/metano/issues/268), [#269](https://github.com/danfma/metano/issues/269), [#270](https://github.com/danfma/metano/issues/270)) ([2df3b26](https://github.com/danfma/metano/commit/2df3b266ef2a89f490ca8b43485e8b28ca2556ee))
* close critic gaps in the polymorphic json specification ([#268](https://github.com/danfma/metano/issues/268), [#269](https://github.com/danfma/metano/issues/269), [#270](https://github.com/danfma/metano/issues/270)) ([4a0e087](https://github.com/danfma/metano/commit/4a0e087f0a1a4944423ff082773aa3b96059adfe))
* close review gaps in the C# unions specification ([#304](https://github.com/danfma/metano/issues/304)) ([49ef112](https://github.com/danfma/metano/commit/49ef11294a92f16d1022b8df44a9f970ee214632)), closes [#278](https://github.com/danfma/metano/issues/278)
* record where TS-shaped lowerings belong ([#320](https://github.com/danfma/metano/issues/320)) ([16d6d5a](https://github.com/danfma/metano/commit/16d6d5ab8d6ff43bce8b804812cfef025fabbb47))
* specify C# 15 union declarations ([#304](https://github.com/danfma/metano/issues/304)) ([6f27a4c](https://github.com/danfma/metano/commit/6f27a4c261f643a7f8ac169ffbcf81b686d283dc))
* specify STJ-faithful polymorphic JSON, branded factories and value roots ([#268](https://github.com/danfma/metano/issues/268), [#269](https://github.com/danfma/metano/issues/269), [#270](https://github.com/danfma/metano/issues/270)) ([98ec36e](https://github.com/danfma/metano/commit/98ec36e850a57ee2ac2f7d82b4cbb8c48be8e8e6)), closes [#278](https://github.com/danfma/metano/issues/278)

## 2.7.0

_2026-11-01_

### ✨ Features

* add an opt-in exports subpath root to EmitPackage ([#267](https://github.com/danfma/metano/issues/267)) ([c5b8583](https://github.com/danfma/metano/commit/c5b8583351dc5d8befd81456b18da7bcbbde7d92)), closes [package.json#exports](https://github.com/danfma/package.json/issues/exports)

### 🐛 Bug Fixes

* apply BCL mappings through optional chains ([#248](https://github.com/danfma/metano/issues/248)) ([40472d4](https://github.com/danfma/metano/commit/40472d4d8b9c891eae69a43654586f2dab8434a4))
* bind a null-conditional receiver that reads an enclosing placeholder ([#248](https://github.com/danfma/metano/issues/248)) ([648ade9](https://github.com/danfma/metano/commit/648ade9e0c5452f32f7b2bfdafd87ebb4d10a525)), closes [#256](https://github.com/danfma/metano/issues/256)
* bind arguments read before a dropped side effect in constructor mappings ([#254](https://github.com/danfma/metano/issues/254)) ([bd252df](https://github.com/danfma/metano/commit/bd252dfe4c01ad72b325655377917ad309ed01c1))
* build every TypeScript IIFE through one async-aware helper ([#248](https://github.com/danfma/metano/issues/248)) ([95e8dc9](https://github.com/danfma/metano/commit/95e8dc9a8fffa3e9b564462c2efd779f7f73c731))
* classify runtime type tests once for is and as ([#279](https://github.com/danfma/metano/issues/279)) ([8a3eac7](https://github.com/danfma/metano/commit/8a3eac7ba3196da44100f92192253f2b787b9848))
* close remaining MS0013 gaps for patterns and member initializers ([#277](https://github.com/danfma/metano/issues/277)) ([7ff7544](https://github.com/danfma/metano/commit/7ff7544ac8198edb46b1a9acd1a96cfd66c0248b)), closes [#247](https://github.com/danfma/metano/issues/247)
* collapse branded value reads and keep static overloads in branded companions ([#245](https://github.com/danfma/metano/issues/245)) ([87d414d](https://github.com/danfma/metano/commit/87d414dd4ce9264e9d97f4221a4666decc5367c6))
* correct JsTuple type tests, reserved-word member access and inherited JSON properties ([#253](https://github.com/danfma/metano/issues/253)) ([4c1fd32](https://github.com/danfma/metano/commit/4c1fd32bd315b55b019e1df88a81ab523a53bb58))
* derive SerializerContext getter names from one helper ([#250](https://github.com/danfma/metano/issues/250)) ([864e1ad](https://github.com/danfma/metano/commit/864e1ad270318e69401fe9aaae62fcab49427588))
* detect missing compiler tool and add source export condition ([#241](https://github.com/danfma/metano/issues/241)) ([fdfc258](https://github.com/danfma/metano/commit/fdfc258ee955d11e5d73815406f0017afcef774b))
* drop the unresolvable compiler peer dependency from metano-runtime ([#249](https://github.com/danfma/metano/issues/249)) ([d5b5701](https://github.com/danfma/metano/commit/d5b5701a57a05c226df181507f3c0a5d2b684910))
* emit nested types of every accessibility in companion namespaces ([#264](https://github.com/danfma/metano/issues/264)) ([0de7f9a](https://github.com/danfma/metano/commit/0de7f9a601c8290997df9ba9f94b171b3a3c17bb))
* emit user exception classes with their full body ([#246](https://github.com/danfma/metano/issues/246)) ([d94e79b](https://github.com/danfma/metano/commit/d94e79be1fa7721ae05254fec9db20e8260ef724))
* escape control characters in printed TypeScript string literals ([#266](https://github.com/danfma/metano/issues/266)) ([dcbcadc](https://github.com/danfma/metano/commit/dcbcadc38a652931130a334bba938c700415f4fa)), closes [#254](https://github.com/danfma/metano/issues/254)
* escape interpolated strings and map BCL collection constructors ([#254](https://github.com/danfma/metano/issues/254)) ([0d121a8](https://github.com/danfma/metano/commit/0d121a85168783d8e19d12fc422e3e476e753f13))
* exempt generated code, erased interfaces and attributes from MS0013 ([#247](https://github.com/danfma/metano/issues/247)) ([f652e76](https://github.com/danfma/metano/commit/f652e76406143b6d8b112983de931c06f016e4b4))
* extend the runtime smoke test to declaration specifiers ([#249](https://github.com/danfma/metano/issues/249)) ([9b95ac1](https://github.com/danfma/metano/commit/9b95ac15eedbc97828356606265973c25ebc9fde))
* gate branded value reads on primitive-backed wrappers ([#245](https://github.com/danfma/metano/issues/245)) ([3d47c7c](https://github.com/danfma/metano/commit/3d47c7cfd4918d0edb0f618ffe7718204adf70dc))
* harden exception lowering for chains, overrides and side effects ([#246](https://github.com/danfma/metano/issues/246)) ([7e4f8eb](https://github.com/danfma/metano/commit/7e4f8eb465708859d66aeb1e4161626b5dd18cbf))
* import cross-package types referenced only in expression bodies ([#286](https://github.com/danfma/metano/issues/286)) ([2339fff](https://github.com/danfma/metano/commit/2339fff56b94c15096960aeb93837d0b04ba9eae)), closes [#267](https://github.com/danfma/metano/issues/267)
* include reachable types in the generated SerializerContext ([#250](https://github.com/danfma/metano/issues/250)) ([a1f4e2c](https://github.com/danfma/metano/commit/a1f4e2c99c386652eeb83c7687879f3871d4f127)), closes [#268](https://github.com/danfma/metano/issues/268) [#270](https://github.com/danfma/metano/issues/270)
* invalidate the group cache when a dependency version changes ([#299](https://github.com/danfma/metano/issues/299)) ([990a9d0](https://github.com/danfma/metano/commit/990a9d099398a3a1163b477ae7e9446e3f1f0054))
* keep base message for Message overrides and treat cast nulls as absent ([#246](https://github.com/danfma/metano/issues/246)) ([442e33b](https://github.com/danfma/metano/commit/442e33b5c519142e51d4d91e8112bd5f98bc314c)), closes [#baseMessage](https://github.com/danfma/metano/issues/baseMessage)
* keep C# null for optional-chain results and nullable patterns ([#256](https://github.com/danfma/metano/issues/256)) ([cd14e75](https://github.com/danfma/metano/commit/cd14e754c286e3ad916c8af7b14a5816a475931e)), closes [#265](https://github.com/danfma/metano/issues/265)
* keep erased interfaces' bases and police emitting attributes in MS0013 ([#247](https://github.com/danfma/metano/issues/247)) ([73cd358](https://github.com/danfma/metano/commit/73cd358e65d20c4139b0a26f600d87bfb40f651c))
* keep JsTuple element reads optional through null-conditional access ([#248](https://github.com/danfma/metano/issues/248)) ([3859092](https://github.com/danfma/metano/commit/385909273fa242acb0d2c80c784453c2bc125896))
* keep Message overrides visible across exception ancestors ([#246](https://github.com/danfma/metano/issues/246)) ([f8b5ddb](https://github.com/danfma/metano/commit/f8b5ddbc0f1770325b9d66e683f32f37776bfc73)), closes [#baseMessage](https://github.com/danfma/metano/issues/baseMessage)
* keep package.json exports when the transpilation cache is fully reused ([#273](https://github.com/danfma/metano/issues/273)) ([be31b84](https://github.com/danfma/metano/commit/be31b84409c03a28fe5e61ca86d612c3b676db91))
* keep the null short-circuit through mapped continuation steps ([#248](https://github.com/danfma/metano/issues/248)) ([b76bb1b](https://github.com/danfma/metano/commit/b76bb1bc94c8ab5861b8449cb9261a9aca78ad56))
* keep the receiver binding when a bound receiver is read past the chain root ([#248](https://github.com/danfma/metano/issues/248)) ([1175a0d](https://github.com/danfma/metano/commit/1175a0d27cf52f1644e10359323451e2dfeecd78))
* lift nullable operators and map Nullable<T> members ([#287](https://github.com/danfma/metano/issues/287)) ([8373fb5](https://github.com/danfma/metano/commit/8373fb5268e41f45ce90d81023049a8bfcecd2a6))
* lower as casts through the type-pattern test ([#279](https://github.com/danfma/metano/issues/279)) ([ac5573f](https://github.com/danfma/metano/commit/ac5573fd6d5a5bd1f9756d3dde5c41235616abbd)), closes [#263](https://github.com/danfma/metano/issues/263) [#263](https://github.com/danfma/metano/issues/263)
* lower derived records and switch-expression patterns correctly ([#238](https://github.com/danfma/metano/issues/238)) ([e9a4cb9](https://github.com/danfma/metano/commit/e9a4cb9046abad8aec24f06a09c02d275510d139))
* lower every null-conditional step through the regular pipeline ([#248](https://github.com/danfma/metano/issues/248)) ([675263d](https://github.com/danfma/metano/commit/675263d7c3b2c8e8a31599a562513996e083edf7))
* lower explicit interface implementations instead of emitting invalid names ([#278](https://github.com/danfma/metano/issues/278)) ([d96909c](https://github.com/danfma/metano/commit/d96909ca06142689e788cf46908433be80ec7d28)), closes [#247](https://github.com/danfma/metano/issues/247)
* lower object and collection initializers instead of dropping them ([#254](https://github.com/danfma/metano/issues/254)) ([02b7779](https://github.com/danfma/metano/commit/02b7779e40ec893646bab9ad87e7b35019673fd3))
* lower out parameters to tuple returns and destructuring call sites ([#265](https://github.com/danfma/metano/issues/265)) ([bf735c9](https://github.com/danfma/metano/commit/bf735c998bc52a42e18bf94d3ebafb46a8a34862))
* lower switch-statement pattern labels and is-pattern designators ([#239](https://github.com/danfma/metano/issues/239)) ([9b7223c](https://github.com/danfma/metano/commit/9b7223cab37b4af7a313f6ab4e664a3b73b0bc86)), closes [#271](https://github.com/danfma/metano/issues/271)
* make metano-runtime importable from Node ESM ([#249](https://github.com/danfma/metano/issues/249)) ([d65b6f3](https://github.com/danfma/metano/commit/d65b6f35880982d74c6d88a71f1fcd3fd663f55c))
* make the runtime smoke test's directory-subpath check fail when it should ([#249](https://github.com/danfma/metano/issues/249)) ([5c58d6c](https://github.com/danfma/metano/commit/5c58d6cca9b1cfa313132e91dd4481723c2e7044))
* map BCL exception constructor arguments by parameter role ([#246](https://github.com/danfma/metano/issues/246)) ([b79424e](https://github.com/danfma/metano/commit/b79424edd29c2f256bfc7b1a2bd59f7e4d478c26))
* map dictionary Keys/Values views and interface dictionary counts ([#295](https://github.com/danfma/metano/issues/295)) ([0fa9f4c](https://github.com/danfma/metano/commit/0fa9f4c7e54ba5227e0b95f6bbcc4f4f7d227652)), closes [#266](https://github.com/danfma/metano/issues/266) [#292](https://github.com/danfma/metano/issues/292)
* map double and float static helpers and special values ([#260](https://github.com/danfma/metano/issues/260)) ([927e1b7](https://github.com/danfma/metano/commit/927e1b7c41d5f63ff803dc697006dfb1c2b1d01f)), closes [#271](https://github.com/danfma/metano/issues/271) [#172](https://github.com/danfma/metano/issues/172) [#255](https://github.com/danfma/metano/issues/255) [#259](https://github.com/danfma/metano/issues/259) [#272](https://github.com/danfma/metano/issues/272)
* map GetValueOrDefault, Math.Round overloads, and string.Join ([#266](https://github.com/danfma/metano/issues/266)) ([e255868](https://github.com/danfma/metano/commit/e25586822794251eb7fc8ce0be50010186481ebf))
* print TypeScript expressions with operator precedence ([#244](https://github.com/danfma/metano/issues/244)) ([6efe8e1](https://github.com/danfma/metano/commit/6efe8e133f82143c41e3091ab23b2e084ddbfa23))
* report exports key collisions at transform time and refine subpath root ([#267](https://github.com/danfma/metano/issues/267)) ([e64b034](https://github.com/danfma/metano/commit/e64b034bfdbf7a69a29dce8ec20135e130c009b4))
* restore cross-package dependencies when package.json is rebuilt from the cache ([#299](https://github.com/danfma/metano/issues/299)) ([408db63](https://github.com/danfma/metano/commit/408db633c2404e884cb12a75360aa49740020d8d))
* support index-from-end and ranges and stop crashing on unsupported expressions ([#263](https://github.com/danfma/metano/issues/263)) ([6538935](https://github.com/danfma/metano/commit/6538935177ae76447d3d493bbe9ee27ed9e85373))
* treat the exponent sign as part of a template number literal ([#272](https://github.com/danfma/metano/issues/272)) ([02209c6](https://github.com/danfma/metano/commit/02209c6b6c083e7422a22b1ddf84aa092e21fd66))
* truncate integer division and keep every for-loop clause ([#307](https://github.com/danfma/metano/issues/307)) ([e344668](https://github.com/danfma/metano/commit/e344668c5e72ff1de636c5e22af2dccc05c1fc9b)), closes [#286](https://github.com/danfma/metano/issues/286)

### ♻️ Refactor

* address review findings on nested-type emission ([#264](https://github.com/danfma/metano/issues/264)) ([3488c71](https://github.com/danfma/metano/commit/3488c71a00640feb4f4749959b2062d2254a7092))
* model null-conditional access on Roslyn's conditional access operation ([#248](https://github.com/danfma/metano/issues/248)) ([3c784d5](https://github.com/danfma/metano/commit/3c784d5ff174d4f0a317923db58565747b3c3540))
* reuse generic IR and evaluate-once binding for exception lowering ([#246](https://github.com/danfma/metano/issues/246)) ([18ff972](https://github.com/danfma/metano/commit/18ff972f96d324022ad11832e5cd66d9b1969d85)), closes [#baseMessage](https://github.com/danfma/metano/issues/baseMessage)
* route every evaluate-once lowering through one binding helper ([#248](https://github.com/danfma/metano/issues/248)) ([47ef676](https://github.com/danfma/metano/commit/47ef6768df8efc82be9d5c706a0e2aca490a38a6))
* split BCL call reshaping by family and type default(TValue) ([#266](https://github.com/danfma/metano/issues/266)) ([20bcf70](https://github.com/danfma/metano/commit/20bcf707477f7d4a186c14e34c9235780388e438))

### 📝 Documentation

* add spec and plan for Node ESM runtime imports ([#249](https://github.com/danfma/metano/issues/249)) ([d188214](https://github.com/danfma/metano/commit/d1882140b42fa954626cbd9d58b4c979c5a4150a))
* align the smoke-test contract with the implemented script ([#249](https://github.com/danfma/metano/issues/249)) ([3cc8407](https://github.com/danfma/metano/commit/3cc840795c510a9c495adc5f519d2775a17c71fd))
* amend MS0013 rule with base-list, attribute and generated-code exemptions ([#247](https://github.com/danfma/metano/issues/247)) ([c2936dc](https://github.com/danfma/metano/commit/c2936dcd129823f22eb251642b313baeae7ef1c7))
* refine MS0013 exemptions for per-target erasure and emitting attributes ([#247](https://github.com/danfma/metano/issues/247)) ([bfa2110](https://github.com/danfma/metano/commit/bfa21108fd687c2ca879c7781c3c7798171136e3))
* specify an opt-in exports subpath root for EmitPackage ([#267](https://github.com/danfma/metano/issues/267)) ([6d76a32](https://github.com/danfma/metano/commit/6d76a326a8ac79d00340172636c738bdabf0f053))

## 2.6.1

_2026-06-29_

### 🐛 Bug Fixes

* import symbols referenced only inside nested record variants ([02c224c](https://github.com/danfma/metano/commit/02c224cb400e11b89285a45fd667a098f16ccf0a))

## 2.6.0

_2026-06-25_

### ✨ Features

* emit full-namespace TypeScript layout with self-cleaning incremental builds ([dd125ef](https://github.com/danfma/metano/commit/dd125ef2ba383b5a749d6ede4485d68935504dda))

### 🐛 Bug Fixes

* harden orphan pruning against null manifest and directory-read failures ([6272918](https://github.com/danfma/metano/commit/6272918e8864c1ef54288af80027dfb9706cbbd1))
* prune stale package.json exports when the last transpilable type is removed ([78eaaf6](https://github.com/danfma/metano/commit/78eaaf67bb4570f0ddc1342cf2ab6391252a5cfc))

### 📝 Documentation

* mark sample-regeneration tasks complete in feature 005 tasks ([8411df2](https://github.com/danfma/metano/commit/8411df2ee40dde2567e4a98e8c8825279fe99c16))
* reconcile baseline matrix and cross-package docs with full-namespace layout ([5fe9c1c](https://github.com/danfma/metano/commit/5fe9c1ccfa5a86844c345067c0b028db59c8d9e1))

## 2.5.0

_2026-06-23_

### ✨ Features

* add configurable isolated subpath-import alias for generated packages ([a0856f2](https://github.com/danfma/metano/commit/a0856f26c4f806259f995d663bcf7815f0052cab))

## 2.4.0

_2026-06-03_

### ✨ Features

* add JS-interop primitives ([JsTuple], [JsCallable], tuple deconstruction) ([9dab074](https://github.com/danfma/metano/commit/9dab07464c3c37756d67d60024d1fed8e1d64828))
* add JSX/TSX code generation from C# components ([6e1279c](https://github.com/danfma/metano/commit/6e1279ce6431a3827d0664c5f42524400252b0f5))
* **build:** incremental skip + ItemGroup args for MetanoTranspile ([#230](https://github.com/danfma/metano/issues/230)) ([b531c94](https://github.com/danfma/metano/commit/b531c94a78edcc7c8746df6f5e262c1ab702084d))
* **compiler:** copy <MetanoAsset> static assets to generated output ([#199](https://github.com/danfma/metano/issues/199)) ([23078f6](https://github.com/danfma/metano/commit/23078f6866bb6a4c14d8f5dbbafdf3db5abd4174))

### 🐛 Bug Fixes

* **build:** consolidate Metano.Build targets + add MetanoEnabled opt-out ([#230](https://github.com/danfma/metano/issues/230)) ([618eede](https://github.com/danfma/metano/commit/618eede3b84eaa2f78f3cddb2fbf97df44d2f36e))
* **compiler:** address PR review findings on asset copy ([#199](https://github.com/danfma/metano/issues/199)) ([a0805cb](https://github.com/danfma/metano/commit/a0805cb308c032ae4095b12487937ebf5f8d4c51))
* correct JSX codegen lambda arity, text escaping, prop-rewrite coverage, and cross-package component imports ([d04ef79](https://github.com/danfma/metano/commit/d04ef79072749d17afaed3a9d5b69a85a3cd6d16)), closes [#002](https://github.com/danfma/metano/issues/002)
* handle IrLinqChain in JSX prop-reference rewrite and regenerate samples ([eb1cccc](https://github.com/danfma/metano/commit/eb1cccc2ec75f8c9b2bc2c23ff66e51bfaca54d3)), closes [#002](https://github.com/danfma/metano/issues/002)
* stop branded records from requesting an unused HashCode helper import ([e8692d2](https://github.com/danfma/metano/commit/e8692d284620065f216e864637f666655256821a))

### ♻️ Refactor

* **annotations:** hygiene sweep across Metano.Annotations ([#229](https://github.com/danfma/metano/issues/229)) ([54c82a1](https://github.com/danfma/metano/commit/54c82a11a8b3273d0087a5d7df28c084d06435e6))
* **compiler:** consolidate fold helpers + Dart duplication ([#225](https://github.com/danfma/metano/issues/225)) ([ced1abb](https://github.com/danfma/metano/commit/ced1abb0822c0e0abe424b81de7bd12969a11f44))
* **compiler:** dispatch JSON descriptor build on JsonPropertyKind enum ([#231](https://github.com/danfma/metano/issues/231)) ([f2efda6](https://github.com/danfma/metano/commit/f2efda6cedf2a0bdcc4e885973b93823d7ecb338))
* **compiler:** extract [Inline] cascade arm into thin rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([5cfe60b](https://github.com/danfma/metano/commit/5cfe60b309096085f14c8586e94701acb38cd428))
* **compiler:** extract Emit/Import template lowering into rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([b996868](https://github.com/danfma/metano/commit/b996868b122a6465ae09b9324a00510329c6eeaf))
* **compiler:** extract extension-method cascade arm into rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([ae84f53](https://github.com/danfma/metano/commit/ae84f53cf815b98d1b3b426664ed665398c344d5))
* **compiler:** extract invocation cascade into rewriter chain ([#221](https://github.com/danfma/metano/issues/221)) ([460b9fb](https://github.com/danfma/metano/commit/460b9fbdaefcb7d4df780867f1821e6f982e505f))
* **compiler:** extract named helpers per naming convention ([e8bb9c2](https://github.com/danfma/metano/commit/e8bb9c2d50618ae91a0fe6d2fbe788470fda690e))
* **compiler:** extract TypeTransformer Parallel.For body ([#222](https://github.com/danfma/metano/issues/222)) ([ad20534](https://github.com/danfma/metano/commit/ad205346fc962d3ae6542aa3f5ffd1ff5ccb037c))
* **compiler:** hoist ImportCollector skip-list into a HashSet ([#224](https://github.com/danfma/metano/issues/224)) ([195dedf](https://github.com/danfma/metano/commit/195dedfc2462361ac9831639cdeaa8af59247c78))
* **compiler:** preserve concrete export kind in per-group cache ([#220](https://github.com/danfma/metano/issues/220)) ([fd29f23](https://github.com/danfma/metano/commit/fd29f23c6c8723396bb9c52c4d7dbbe6fc113f6f))
* **compiler:** split IrExpressionExtractor into themed partials ([#221](https://github.com/danfma/metano/issues/221)) ([22d691b](https://github.com/danfma/metano/commit/22d691b50a4b4053018fa2640083e35a1a5196fd))
* **compiler:** split IrToTsClassBridge into thematic partial files ([#223](https://github.com/danfma/metano/issues/223)) ([9c52ee8](https://github.com/danfma/metano/commit/9c52ee854554bfd03d9b5009c1b985fe1e77fe1f))
* **compiler:** split IrToTsClassEmitter.Transform into named phases ([#223](https://github.com/danfma/metano/issues/223)) ([4ff44fd](https://github.com/danfma/metano/commit/4ff44fd5265cb06dc40692697f64e2c54b5c5fc6))
* **compiler:** split TypeTransformer.BuildTypeStatements + drains ([#222](https://github.com/danfma/metano/issues/222)) ([5ef3b6f](https://github.com/danfma/metano/commit/5ef3b6fba5fbf427db010bacbd73c252e707dfc4))
* **compiler:** unify diagnostics sink discipline across TS + Dart ([#226](https://github.com/danfma/metano/issues/226)) ([15a9d52](https://github.com/danfma/metano/commit/15a9d52a13b2147814ef59e3d36c35b4020e5395)), closes [#218](https://github.com/danfma/metano/issues/218)
* migrate SolidJS signal binding onto JS-interop primitives ([6189a5e](https://github.com/danfma/metano/commit/6189a5e660b102d427c9f448bc72cbd76756dc37))

### 📝 Documentation

* **compiler:** refresh stale caching/annotation docstrings ([#232](https://github.com/danfma/metano/issues/232)) ([f0fc47b](https://github.com/danfma/metano/commit/f0fc47bb29a4b32334411cd21de7e11174b90b0f))
* ratify constitution and add baseline spec with multi-target evolution roadmap ([6af0d36](https://github.com/danfma/metano/commit/6af0d368e84b514bd38cbbd45fa77908ece0be4e))
* reconcile plan/tasks signal-lowering with shipped feature-003 primitives ([7bc2667](https://github.com/danfma/metano/commit/7bc266780d3d075d81f3f16b8b5416411ef547ac))
* retire old-spec after baseline parity sign-off ([2ed272f](https://github.com/danfma/metano/commit/2ed272f77b6affe6f90adadeb6cc8d2125a98fec))

## 2.3.0

_2026-05-12_

### ✨ Features

* **compiler:** --watch mode ([#18](https://github.com/danfma/metano/issues/18)) ([8fff5a7](https://github.com/danfma/metano/commit/8fff5a7e32db3acfd830f8148d114fb584fe02d9)), closes [#21](https://github.com/danfma/metano/issues/21) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#214](https://github.com/danfma/metano/issues/214)
* **compiler:** add [StrictUnionGuard] for shape-level union dispatch ([#154](https://github.com/danfma/metano/issues/154)) ([f09fc40](https://github.com/danfma/metano/commit/f09fc402723912a85982bfde08348850b1c925da)), closes [#88](https://github.com/danfma/metano/issues/88)
* **compiler:** broaden MS0024 trigger to non-LINQ [Queryable] callsites ([#218](https://github.com/danfma/metano/issues/218)) ([7c47a44](https://github.com/danfma/metano/commit/7c47a44ea474077bcec11af93d0abd1a89491770))
* **compiler:** emit qualified type refs in queryable expression trees ([eae3507](https://github.com/danfma/metano/commit/eae3507fffcfb2d66b48d319dc94c588544022b7)), closes [#203](https://github.com/danfma/metano/issues/203)
* **compiler:** extend queryable walker with new + nested lambda ([#206](https://github.com/danfma/metano/issues/206)) ([e633d2f](https://github.com/danfma/metano/commit/e633d2fe5b61f8b913390327bdb712b3e79daf34))
* **compiler:** fold static readonly + pure-arithmetic captures ([#208](https://github.com/danfma/metano/issues/208)) ([0a7d251](https://github.com/danfma/metano/commit/0a7d251ff60e06c0b915674b717757564743f0be))
* **compiler:** fuse adjacent LINQ stages at build time ([#207](https://github.com/danfma/metano/issues/207)) ([bf48474](https://github.com/danfma/metano/commit/bf484742bac5e70605c54a20d5be72c990b4361a))
* **compiler:** group closure hasher for PR 3b ([8b0c270](https://github.com/danfma/metano/commit/8b0c2703cbabdd7408e15b36f5f26acbab33704e))
* **compiler:** hoist pure repeated subtrees out of captured ExprTrees ([#209](https://github.com/danfma/metano/issues/209)) ([46ad6da](https://github.com/danfma/metano/commit/46ad6da689fe8a64762f97d15be4296a65276590))
* **compiler:** incremental cache MVP — whole-build short-circuit (ADR-0021) ([9eef85a](https://github.com/danfma/metano/commit/9eef85a3fa249b3f31b2c12d577d34525385c727)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#18](https://github.com/danfma/metano/issues/18) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#18](https://github.com/danfma/metano/issues/18)
* **compiler:** lower extension indexers via item$get / item$set helpers ([#156](https://github.com/danfma/metano/issues/156)) ([7bfe7dd](https://github.com/danfma/metano/commit/7bfe7dd2a33b88a190b7786faad71a72be9b149f))
* **compiler:** lower extension property setters via $set helpers ([#156](https://github.com/danfma/metano/issues/156)) ([6768eb5](https://github.com/danfma/metano/commit/6768eb58e26bf613d8d9c101a4cd1a4f6adf8aaa))
* **compiler:** lower static extension members to module helpers ([#156](https://github.com/danfma/metano/issues/156)) ([457d2b5](https://github.com/danfma/metano/commit/457d2b537b1bfb93cb7791ec59c8016180007bb2))
* **compiler:** MS0024 hard error for explicit queryable opt-in ([#205](https://github.com/danfma/metano/issues/205)) ([95ca446](https://github.com/danfma/metano/commit/95ca446a7d2845fcf0e5cfb48d5fc2f8001aac07))
* **compiler:** parallelize TypeTransformer per file group (ADR-0020) ([b37c6d2](https://github.com/danfma/metano/commit/b37c6d2cae5336c7e6621fd3dabc0228dd91cfdb)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#211](https://github.com/danfma/metano/issues/211) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18)
* **compiler:** per-group skip integration (PR 3c) ([6dd58b6](https://github.com/danfma/metano/commit/6dd58b61788d5c9f377b01902a9ccb4c5486df83)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#214](https://github.com/danfma/metano/issues/214) [#215](https://github.com/danfma/metano/issues/215) [#216](https://github.com/danfma/metano/issues/216)
* **compiler:** per-type signature hasher for PR 3b ([590a5c6](https://github.com/danfma/metano/commit/590a5c6f579e7badf9f875d35f5d734bae75cafc))
* **compiler:** type-level dependency graph backbone for [#18](https://github.com/danfma/metano/issues/18) + [#21](https://github.com/danfma/metano/issues/21) ([fcf999f](https://github.com/danfma/metano/commit/fcf999fc9fc6fe8098b1aa395451ee7172b0ee86))
* **runtime:** migrate array provider to getStages + document linq slot ([#200](https://github.com/danfma/metano/issues/200)) ([b5f0101](https://github.com/danfma/metano/commit/b5f0101f3e079a73d646d6d54ddfbc6113b62895))
* **runtime:** publish ExprTree visitor API for queryable providers ([f77fa13](https://github.com/danfma/metano/commit/f77fa1370fbfc6f45511e00dfa7b4c94c4284c53)), closes [#198](https://github.com/danfma/metano/issues/198)

### 🐛 Bug Fixes

* address PR [#204](https://github.com/danfma/metano/issues/204) review + add pre-push targets/ sync check ([6815397](https://github.com/danfma/metano/commit/681539750b60be6ee99d78b30e600f3e6bf6e8ed)), closes [C#-style](https://github.com/danfma/C/issues/-style)
* **compiler,runtime:** record equals() routes value-wrapper fields through valueEquals ([#202](https://github.com/danfma/metano/issues/202)) ([3fd0659](https://github.com/danfma/metano/commit/3fd0659275818ef2940f0a8913164b70c267404e))
* **compiler:** gate per-group cache writes on disk-touching runs ([9776c13](https://github.com/danfma/metano/commit/9776c13340dfb83253246d38cfd1cb8b72b35260)), closes [#217](https://github.com/danfma/metano/issues/217)
* **compiler:** harden per-group cache against config drift, tampering, and error runs ([669755f](https://github.com/danfma/metano/commit/669755fadb08be396895457eac2327dcd38d782b)), closes [#217](https://github.com/danfma/metano/issues/217)
* **compiler:** make parallel TypeTransformer's shared sinks actually thread-safe ([4147f83](https://github.com/danfma/metano/commit/4147f83275cacf0b5065893b3b60deca89b9dde2)), closes [#213](https://github.com/danfma/metano/issues/213)
* **compiler:** stabilise IrTypeSignatureHasher cache key (gemini review) ([e2a246c](https://github.com/danfma/metano/commit/e2a246cdedd08812d881388d4637b71311adb147)), closes [#216](https://github.com/danfma/metano/issues/216)
* **compiler:** tighten incremental cache key + harden cache reads ([47baaed](https://github.com/danfma/metano/commit/47baaed4b40b270f1838626d8d5656f9d096e420)), closes [#214](https://github.com/danfma/metano/issues/214)
* **compiler:** tighten WatchHost debounce + sync per Copilot review ([6e89b62](https://github.com/danfma/metano/commit/6e89b62dc0f8e9ff2d5939c3f0c03744ebffb1ec)), closes [#215](https://github.com/danfma/metano/issues/215)
* **compiler:** tighten WatchHost rename + error handling, unsubscribe Ctrl+C handler ([695002b](https://github.com/danfma/metano/commit/695002bd6422852cbb6c1b911f975355dfc53857)), closes [#215](https://github.com/danfma/metano/issues/215)
* **compiler:** verify WatchHost review findings already landed ([9212907](https://github.com/danfma/metano/commit/9212907fbc546da2570400d6f30908d97bc3a415))
* **dependency-graph:** address PR [#211](https://github.com/danfma/metano/issues/211) review findings ([4fce313](https://github.com/danfma/metano/commit/4fce313656af2406c06ab760e3c174df8a6ff99e))
* **reorg:** correct cref namespaces + harden pre-push gate against build races ([7b88091](https://github.com/danfma/metano/commit/7b8809110fc54f1cabe3ecb9fafb9b0cc74fec65))

### ♻️ Refactor

* **compiler:** route ExprTree member/method casing through target-aware policy ([#210](https://github.com/danfma/metano/issues/210)) ([21f9713](https://github.com/danfma/metano/commit/21f9713d13c5a7e4a297fee5ec70ca9906a55f83))
* **compiler:** split Metano.Compiler folders by concern (ADR-0019) ([a16aafb](https://github.com/danfma/metano/commit/a16aafbfff7f29f55b955810428f9f045b7836f5))
* **namespaces:** nest Metano.{Dart,TypeScript}.* under Metano.Compiler.* ([6276672](https://github.com/danfma/metano/commit/62766726fa477a903a452cfb49c7d88ee6d920b3))

### 📝 Documentation

* ADR-0023 — per-group skip foundation (PR 3b setup) ([ecb81db](https://github.com/danfma/metano/commit/ecb81dbcb239e129f32c8b1b48fee777192a663f))
* **dependency-graph:** scope statement to signature surface only ([ed921d5](https://github.com/danfma/metano/commit/ed921d56549e839adb58ee531b12ea232d884899))
* **plans:** seed reorganization brief ([208cb95](https://github.com/danfma/metano/commit/208cb951ddefa47c19267edcb43bbfe1f1a5bce2))
* **roadmap:** editorial charter so docs/roadmap stays focused ([abf809d](https://github.com/danfma/metano/commit/abf809d133be762f7e0500cb5867b2c0c5a46847))

## 2.2.0

_2026-05-07_

### ✨ Features

* **compiler,ts:** Phase A — lower LINQ chains to pipe form ([#20](https://github.com/danfma/metano/issues/20)) ([73cd8f5](https://github.com/danfma/metano/commit/73cd8f501ebbf9a6e889818bc553bc7c56d6253d)), closes [#31](https://github.com/danfma/metano/issues/31)
* **compiler,ts:** Phase B — capture lambda body as IR expression tree for IQueryable ([#31](https://github.com/danfma/metano/issues/31)) ([d18acf3](https://github.com/danfma/metano/commit/d18acf3f8313cb70836c7288bbb88e007a75ce09))
* **metano-runtime,annotations:** add ExprTree slot + [Queryable] attribute ([#20](https://github.com/danfma/metano/issues/20) → [#31](https://github.com/danfma/metano/issues/31) prep) ([15c0157](https://github.com/danfma/metano/commit/15c015783841d5c5e46bba01b1dd7f7522f4d444))
* **metano-runtime,compiler:** replace legacy LINQ with pipe runtime + groupBy + 32 overloads ([#20](https://github.com/danfma/metano/issues/20)) ([1462c04](https://github.com/danfma/metano/commit/1462c048630948fdcc095cf791c6c8c73575a17e))
* **metano-runtime:** linq-pipe operators wrap generator behind Symbol.iterator ([#20](https://github.com/danfma/metano/issues/20)) ([ed81252](https://github.com/danfma/metano/commit/ed81252c014e3fb1602b94124f2685a1e30f270e))
* **metano-runtime:** prototype pipe-based LINQ runtime ([#20](https://github.com/danfma/metano/issues/20)) ([92715a5](https://github.com/danfma/metano/commit/92715a5ec8acbaf4ad4c73ae9dd2e28bfb254b1c))
* **sample,runtime,compiler:** SQLite EF-lite provider via captured expression trees ([#198](https://github.com/danfma/metano/issues/198), [#200](https://github.com/danfma/metano/issues/200)) ([64ad51b](https://github.com/danfma/metano/commit/64ad51bd33cdec3f9efa26a897887bf0ea5e2b16))
* **sample:** demo IQueryable provider over arrays via Phase B trees ([#31](https://github.com/danfma/metano/issues/31)) ([59b3ebb](https://github.com/danfma/metano/commit/59b3ebbf638570656a587ca13c1ec25d28f0159c)), closes [#196](https://github.com/danfma/metano/issues/196)
* **sample:** log emitted SQL via optional logger callback ([86b1592](https://github.com/danfma/metano/commit/86b159220d8046801e2d3d72af42f9b0d93ffaf0))

### 🐛 Bug Fixes

* **compiler,sample:** address PR [#197](https://github.com/danfma/metano/issues/197) review findings ([d17b86d](https://github.com/danfma/metano/commit/d17b86d4527f63524b40b02ebad1f7669a513ecd))
* **compiler,ts:** address PR [#196](https://github.com/danfma/metano/issues/196) review findings ([3ae80a3](https://github.com/danfma/metano/commit/3ae80a3b5fdbbd79cbcf9985a15da53c3c964bd4))
* **metano-runtime,compiler:** address PR [#195](https://github.com/danfma/metano/issues/195) review findings ([6652f9f](https://github.com/danfma/metano/commit/6652f9fd8de530ebd1e2bd142089c097221255f9))
* **metano-runtime:** export Grouping and GroupByOp types from linq barrel ([4ed1ae5](https://github.com/danfma/metano/commit/4ed1ae5686c12e7282a59db6e74595a0fd50d53e))
* **sample,compiler:** address PR [#201](https://github.com/danfma/metano/issues/201) review findings ([a49efe8](https://github.com/danfma/metano/commit/a49efe8312c4dd04c92e63a98afeb637d00d2679))
* **sample:** biome compliance + main.ts entry for bun run . ([c0272bb](https://github.com/danfma/metano/commit/c0272bb6a992daf194f4cbbe18802bc8ed116b61)), closes [C#-emitted](https://github.com/danfma/C/issues/-emitted)

### ♻️ Refactor

* **metano-runtime:** linq-pipe operators return tagged descriptors ([#20](https://github.com/danfma/metano/issues/20)) ([47bbe7c](https://github.com/danfma/metano/commit/47bbe7c448511bef1edd5e2aaeed79a336546de2))
* **metano-runtime:** nest queryable meta + add ExprCapture ([#20](https://github.com/danfma/metano/issues/20) → [#31](https://github.com/danfma/metano/issues/31)) ([7e635eb](https://github.com/danfma/metano/commit/7e635ebf99095ef11471030ba66a0589202e560b)), closes [#5](https://github.com/danfma/metano/issues/5) [#6](https://github.com/danfma/metano/issues/6)

### 📝 Documentation

* **linq:** fix stale linq-pipe paths + expression field name in PR [#195](https://github.com/danfma/metano/issues/195) review ([f39ede1](https://github.com/danfma/metano/commit/f39ede10c73839a81f2ddde93664e195523152f1))

## 2.1.0

_2026-05-02_

### ✨ Features

* **cli:** add --dry-run flag ([#19](https://github.com/danfma/metano/issues/19)) ([d035bba](https://github.com/danfma/metano/commit/d035bba173076abb3c8b6fd26f5db80419542207))
* **ts:** support direct recursion in [Inline] methods via named function expression ([#194](https://github.com/danfma/metano/issues/194)) ([04f5813](https://github.com/danfma/metano/commit/04f5813c3ba1dcfd79e1978980399fcb69cab9c1))

### 🐛 Bug Fixes

* **compiler:** normalize args at [Emit] template call site ([#192](https://github.com/danfma/metano/issues/192)) ([221f54d](https://github.com/danfma/metano/commit/221f54dac25da209f79ccc61ac6277cce036e110))
* **compiler:** propagate [Name(target, ...)] through method/interface type references ([#170](https://github.com/danfma/metano/issues/170)) ([4cc97be](https://github.com/danfma/metano/commit/4cc97be9bd614843f3e0d78ee18529ec1a10afe4))
* **import-collector:** register [NoContainer] method-group references for import ([#179](https://github.com/danfma/metano/issues/179)) ([b88595e](https://github.com/danfma/metano/commit/b88595e81e3e2930fe6728329389a21b5417551c))
* **import-collector:** scan referenced assemblies for [NoContainer] exports ([#178](https://github.com/danfma/metano/issues/178)) ([f171da5](https://github.com/danfma/metano/commit/f171da59a9f40ff1ae5696df65fdccf5d6582ebf))
* **ts:** drop parameter-property modifiers on dispatcher overload sigs + rest impl ([#25](https://github.com/danfma/metano/issues/25)) ([d5e2f6d](https://github.com/danfma/metano/commit/d5e2f6dfbabca88324ac7fb86650123b854aa03b))
* **ts:** split record positional params into field + rest ctor parameter ([#152](https://github.com/danfma/metano/issues/152)) ([dbf3fc3](https://github.com/danfma/metano/commit/dbf3fc3bbc5ecb1c5b601e2a542102a09a5f74e4)), closes [#145](https://github.com/danfma/metano/issues/145)

### ♻️ Refactor

* **import-collector:** bundle walker buckets into ImportCollectionSink ([#192](https://github.com/danfma/metano/issues/192)) ([9a942d2](https://github.com/danfma/metano/commit/9a942d2dfc93bdcf5bab9a761439c6b2a6471fe7))

### 📝 Documentation

* **annotations:** pin [External] + [Import] resolution recipe ([#190](https://github.com/danfma/metano/issues/190)) ([285ceb2](https://github.com/danfma/metano/commit/285ceb25d955de47261909e89c6ed4dc65770bff))

## 2.0.0

_2026-05-01_

### ⚠ BREAKING CHANGES

* **annotations:** [NoEmit] and [NoTranspile] are removed. Replace any
[NoEmit] usage with [Ignore] (semantics carry over). Replace any
[NoTranspile] with [Ignore] when the type is .NET-only, or with [External]
when it is an ambient TypeScript shape that transpiled code legitimately
references. Member-level [NoTranspile] without a replacement should switch
to member-level [Ignore].

Compiler changes:

- ValidateIgnoreReferences now skips members marked [Ignore] (an [Ignore]
  helper that takes an [Ignore] marker stays silent).
- Member-level reference validation: invocations and member access on
  [Ignore] methods/properties/fields/events from transpilable code raise
  MS0013 with the member's owner.member name in the message.
- IrTypeRefMapper threads the active TargetLanguage through HasIgnore so a
  type ignored only for the active backend is correctly marked
  IsIgnored=true in the IR (and IsTranspilable=false), keeping downstream
  paths like lambda parameter lowering and runtime guard generation
  consistent with the per-target contract.
- IR field IrNamedTypeSemantics.IsNoEmit renamed to IsIgnored.
- DiagnosticCodes.NoEmitReferencedByTranspiledCode renamed to
  IgnoreReferencedByTranspiledCode (the MS0013 code itself is unchanged).

Tests:

- New IgnoreDotNetOnlyTests pin the MS0013 contract on every reference
  position (parameter, return, field type, body, generic argument,
  per-target gating).
- Former NoEmitTranspileTests renamed to ExternalAmbientTranspileTests; the
  ambient cases all reference [External] now, matching the new semantics.
- Test methods disambiguated (Ignore_ExcludesTypeFromAssemblyWideTranspile,
  Ignore_OverridesExplicitTranspileAttribute, IgnorePerTarget_*).

### ✨ Features

* **annotations:** collapse [NoEmit]/[NoTranspile]/[Ignore] into single [Ignore] ([06b5e43](https://github.com/danfma/metano/commit/06b5e438abe878819f5e5f0d85ba37a9c9d97f04))

### 🐛 Bug Fixes

* **ts:** align import-type form with Biome's expectation ([5714185](https://github.com/danfma/metano/commit/5714185cfc2bca810e2db72552ca2da322e40b08))

## 1.1.0

_2026-05-01_

### ✨ Features

* **compiler:** enhance type checking, import handling, and constructor extraction ([0ec6081](https://github.com/danfma/metano/commit/0ec60812ffbe5dfbe33dd95f78864cd6347651be))

## 1.0.3

_2026-04-30_

### 🐛 Bug Fixes

* **release:** use RELEASE_TAG instead of GITHUB_REF_NAME for npm publish ([13db217](https://github.com/danfma/metano/commit/13db2176009b9c4c115cd0a09f1eac8b3149db5b))

## 1.0.2

_2026-04-30_

### ♻️ Refactor

* **release:** drop dotnet-releaser, use dotnet pack + nuget push directly ([f6c98af](https://github.com/danfma/metano/commit/f6c98af567533b072d63ce5e1747c83a0b250f8f))

## 1.0.1

_2026-04-30_

### 🐛 Bug Fixes

* **release:** install npm deps before metano-runtime build in publish-on-tag ([feba5dc](https://github.com/danfma/metano/commit/feba5dc15fa2302d2e3c8a95783284706f6f27cb))
* **release:** split publish-on-tag into independent NuGet and npm jobs ([7b1a5d2](https://github.com/danfma/metano/commit/7b1a5d23c1468336dc6b45c67a9fd1e240e1d85b))

## 1.0.0

_2026-04-30_

### ✨ Features

* **compiler:** add --file-prefix CLI flag for opaque generated-file headers ([8c05ec1](https://github.com/danfma/metano/commit/8c05ec19d63ad0fb82ea4aae92686ce7357531ee))
* **ir:** materialize [Inline] method as lambda when passed as value ([129661d](https://github.com/danfma/metano/commit/129661de3ca3ece36eb43bce807be0369eb638a9)), closes [#193](https://github.com/danfma/metano/issues/193)

### ♻️ Refactor

* **annotations:** rename [Erasable] → [NoContainer] + add InlineMode ([358fb37](https://github.com/danfma/metano/commit/358fb37985ac827e49eba757b9efda618227f1ed))

### 📝 Documentation

* add ADR-0017 + update spec catalogs for [NoContainer] / InlineMode ([31859c6](https://github.com/danfma/metano/commit/31859c6d7acf7187f588b3d0918c7410992ca7ed))

## 0.9.0

### ✨ Features

- **Dart target prototype.** New `Metano.Compiler.Dart` project ships
  the shape-only Dart backend: `metano_runtime` Dart package with
  hashing/equality primitives, `MetanoObject` base injection,
  `DartImportCollector` wired to runtime requirements, declarative
  BCL mappings via target-aware `[MapMethod]` attributes, and Dart
  delegate lowering to typedefs.
- **`[ObjectArgs]` family.** Object-literal call shape now covers
  static methods, instance methods, and constructors via a `create`
  factory pattern (#163, #167). Type arguments survive the lowering
  (#169) and trailing `params` arguments fold into an array literal
  (#186).
- **`[Emit]` template `$T0` placeholder.** Generic type arguments
  splice into the lowered template verbatim — `Foo.Of<Bar>(...)`
  emits with `Bar` in the template body. Closes #189.
- **Method-level `[Import]` lowering.** A method annotated with
  `[Import(name, from)]` now lowers every call site to a direct
  invocation of the imported identifier and auto-emits the import
  line on the consumer file. The declaring class no longer emits a
  stub. Pairs with `[Emit]` for templated facades. Closes #188.
- **`[ImportAlias]` attribute.** File-scoped TS module carrier for
  picking the import binding name when the C# type's name collides
  (#184). Complements automatic alias synthesis when an erasable
  factory shadows a transpilable type (#183) and propagation of C#
  `using X = Y;` aliases through to TS imports (#182).
- **TypeScript class inheritance.** `extends` + `abstract` modifiers
  emit on the class surface (#118). Sealed hierarchies emit a union
  guard built around a shared discriminator (#88).
- **Extension helper lowering.** Transpilable extension members now
  lower to helper calls at the call site (#156); names propagate
  through `[Name]` and clashes between extension classes raise the
  new MS0021 diagnostic.
- **Function shape coverage.** Default parameter values emit on
  methods and functions (#115). Named arguments reorder into
  declaration order (#157). C# `params` map to TS rest parameters
  (#145). `[Transpile]` delegates emit named type aliases (#122).
- **Internal-visibility surface.** `internal` members now reach the
  TS class output instead of being silently dropped (#162).
- **`[Inline]` propagation.** A static class marked `[Inline]`
  propagates the marker to every member, removing the per-member
  bookkeeping (#107).
- **`[Erasable]` diagnostic — MS0020.** Two `[Erasable]` factories
  resolving to the same emitted name surface as a hard error
  pointing at both definitions.
- **`[NoEmit]` / `[External]` redefinition.** `[NoEmit]` becomes a
  pure .NET-only painting marker; `[External]` widens to cover every
  ambient binding shape that previously leaned on `[NoEmit]`. Class-
  level flatten dropped from `[External]` — flatten now requires
  `[Erasable]` opt-in (#106 PR1–5). MS0013 surfaces misuse.
- **DOM bindings library.** New `Metano.TypeScript.DOM` project
  exposes `Document`, `Window`, `HtmlElement`, etc. as `[External]`
  ambient classes; `Js.Document` / `Js.Window` provide `[Erasable]`
  globals shortcuts.
- **SampleCounterV3 + V4 + V5.** V3 reworked as a mini-MVU/Flutter
  DSL. V4 wires a Flutter-style widget facade through `[Erasable]`
  + `[ObjectArgs]`. V5 ships an Inferno virtual-DOM consumer end-to-
  end with a JSX-flavored widget DSL.

### 🐛 Bug Fixes

- C# 14 `extension(R r) { … }` members now emit once instead of twice
  (Roslyn surfaces them both lifted on the static class and inside
  a synthetic empty-name nested type).
- IR fixes: throw expressions lower to an IIFE with a throw statement
  (#160); `new T()` on a generic type parameter raises MS0019 (#161);
  primary-constructor parameter rewrites cover switch/argument/local-
  var positions (#158); override methods are segregated from sibling
  overload groups (#159); abstract method parameter initializers are
  dropped (#147); abstract modifier is suppressed on records (#144);
  default initializers are dropped when a constructor parameter
  default already covers the field (#164, #165).
- TS imports collected through function, tuple, and type-predicate
  types (#148).

### 🧰 Maintenance

- Sample regeneration is now part of CI: `dotnet build` followed by
  `bunx biome format --write targets/` produces the canonical sample
  output and the drift check diffs against it.
- `Metano-packages.slnx` shipped as a solution alias scoped to the
  publishable projects so the release pipeline avoids file-lock
  conflicts with sample `AfterBuild` targets.

## 0.8.1

### 🐛 Bug Fixes

- `package.json` writes are now additive: hand-curated `type`,
  `sideEffects`, `name`, `imports`, and `exports` survive every
  regeneration. The transpiler only seeds missing fields and refreshes
  its own `{types, import}` exports, leaving user-added subpaths and
  augmented conditional fields untouched (#136).
- `metano-runtime` now declares `"sideEffects": false` so consumers'
  bundlers can tree-shake unused helpers. Verified bundle drop from
  175.1 KB to 1.96 KB on a `HashCode`-only entry — 88× smaller (#137).

### 🧰 Maintenance

- `dotnet-releaser.toml` adds Conventional Commits autolabelers so
  `feat:` / `fix:` / `chore:` etc. land in the right release-note
  sections instead of bundling under "🧰 Misc" (#135).
- Release workflow grants `pull-requests: write` and `issues: write`
  so dotnet-releaser can read merged-PR data when assembling the
  changelog.
- Pinned `dotnet-releaser` 0.16.0 → 0.18.1 for the Tomlyn config
  deserialization fix.
- Switched to a static `CHANGELOG.md` driving release notes —
  works around the GitHub `/commits/{sha}/pulls` 5xx flakiness that
  blocked the v0.8.1 changelog auto-generation.

## 0.8.0

See [the v0.8.0 release notes](https://github.com/danfma/metano/releases/tag/v0.8.0)
for the prior changeset.
