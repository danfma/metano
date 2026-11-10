# Changelog

All notable changes to Metano are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased (3.0.0 migration notes)

Pre-release notes for the 3.0.0 breaking changes that are already merged;
the release regenerates the released sections, and these notes carry forward
the migration list.

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
  intPairs)` rebuilds each element the same way. A converted string spread
  (`List<long> codes = [.. text];`) reports `MS0001`.
- An operator group whose overloads return different types (`int operator
  +(M, int)` beside `long operator +(M, long)`) declares its dispatcher
  implementation as `any`, as method dispatchers do; 2.x declared the first
  overload's type, which did not type-check. Overloads that return one type
  keep it.
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
  no default gives `T | null`, a `T` default gives `T`, a `T | null` default
  gives `T | null`. Hand-written calls keep compiling, with or without an
  explicit element type argument, as long as the element type comes from the
  surrounding `linq(...)` call or a type argument: the default value no
  longer infers it, so a detached `firstOrDefault(undefined, 0)` is a term
  over `unknown` (write `firstOrDefault<number>(undefined, 0)`), and a
  default of another type is rejected. The declarations use `NoInfer`, so
  type-checking against `metano-runtime` needs TypeScript 5.4 or later.
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
  an `IrSpreadExpression`; no extraction option turns them off.
- The cost is documented and benchmarked (`bun run bench:int64`); code that
  does not need 64-bit range should keep `int` or `double`.

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
  metadata-only reference keeps the dispatcher calls.
- Dart (experimental): overloaded constructors become named constructors
  (`Dimension.fromSize`) and overloaded methods fast paths (`addX`), with
  direct call sites, instead of the primary overload alone; `[Name]` on an
  overload pins its suffix there too, and call sites spell a method's `[Name]`.

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
