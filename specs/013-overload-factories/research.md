# Research: Overload factories, selector symbols and direct call sites

Evidence tags: **[V]** = verified on 2026-11-04, either by reading the cited file on this branch
(rebased on `f520be36`, which includes #473 / slice 012a) or by running the cited probe; **[I]** =
inferred from code or docs, not executed; **[M]** = maintainer decision.

Probe environment [V]: macOS arm64, .NET SDK 10.0.401, `tsgo` 7.0.0-dev.20260707.2 (the pinned
preview in `targets/js/metano-runtime`), Node v24.11.0, Bun 1.4.2. The probe programs live in the
session scratchpad and are not committed (round 1: `glm25/`; review: `rev013/p1`–`p3`; round 2:
`s013r2/a`–`h`, `s013r2/r7`, `s013r2/today`). Every output line below is quoted verbatim. Unless
stated otherwise, the TypeScript probes compile with
`tsgo --strict --declaration --target es2022 --module nodenext --moduleResolution nodenext` and run
the emitted JavaScript under Node and Bun.

## R1 — Selector signatures and plain signatures coexist; the `.d.ts` keeps both

Probe `s013r2/b/money.ts` [V]: `class Money` with the type-qualified selectors of DECIDED-16
(`protected static readonly byMoneyAmount: unique symbol = Symbol("Money(decimal amount)")`,
`byMoneyCents`), two selector overload signatures, the retained plain signature
`constructor(amount: number)`, a `(...args: unknown[])` implementation whose selector branches are
guarded by `typeof args[0] === "symbol" &&` (FR-009), and two factories. `exit=0`. The emitted
`money.d.ts`:

```ts
export declare class Money {
    protected static readonly byMoneyAmount: unique symbol;
    protected static readonly byMoneyCents: unique symbol;
    static readonly Zero: Money;
    readonly v: number;
    constructor(overload: typeof Money.byMoneyAmount, amount: number);
    constructor(overload: typeof Money.byMoneyCents, cents: number);
    constructor(amount: number);
    static fromAmount(amount: number): Money;
    static fromCents(cents: number): Money;
}
```

- The selector signatures and the plain signature sit side by side in the source and in the
  `.d.ts`, so a hand-written `new Money(2)` keeps type-checking (Node prints `200` for it, R2).
- `protected` works: `typeof Money.byMoneyAmount` is legal inside the class body, and the `.d.ts`
  keeps the protected symbols.
- The same files pass with `--isolatedModules --verbatimModuleSyntax --module esnext
  --moduleResolution bundler` (`exit=0`) [V].

## R2 — Base, derived and grand-derived classes with identical suffixes

Probe `s013r2/b` [V]: `Money(decimal amount)` / `Money(int cents)`; `Receipt : Money` with
`Receipt(decimal amount) : base(amount)` / `Receipt(int cents) : base(cents)` (identical suffixes
and identical parameter lists); `GiftReceipt : Receipt` with `GiftReceipt(string amount)` /
`GiftReceipt(int cents)` (identical suffixes, but `string amount` differs from the inherited
`fromAmount(amount: number)`). Each class has its own module.

Without type-qualified selectors (the round-1 shape, reviewer probe `rev013/p2/base.ts`) [V]:

```text
p2/base.ts(17,14): error TS2417: Class static side 'typeof Receipt' incorrectly extends base class static side 'typeof Money'.
  Types of property 'byAmount' are incompatible.
```

Two `unique symbol` types are never assignable to each other, so every derived class that repeats
a base suffix breaks the static side. A factory with a different parameter list breaks it the same
way (`rev013/p2/fac.ts`: `Types of property 'fromAmount' are incompatible ... Target signature
provides too few arguments`), and so does `s013r2/b/gift-unqualified.ts`:

```text
gift-unqualified.ts(2,14): error TS2417: Class static side 'typeof GiftReceiptU' incorrectly extends base class static side 'typeof Receipt'.
  Types of property 'fromAmount' are incompatible.
    Type '(amount: string) => GiftReceiptU' is not assignable to type '(amount: number) => Receipt'.
```

With the DECIDED-16 / FR-026 shape — `byMoneyAmount`, `byReceiptAmount`, `byGiftReceiptAmount`;
`Receipt.fromAmount(amount: number)` kept (same parameter list as the inherited factory);
`GiftReceipt.fromGiftReceiptAmount(amount: string)` qualified (different list) and
`GiftReceipt.fromCents(cents: number)` kept — the three modules compile (`exit=0`, also under the
isolated / bundler flags) and run:

```text
== node
0 150 200 250 receipt-amount 0 ten 7 Receipt
== bun
0 150 200 250 receipt-amount 0 ten 7 Receipt
```

The last value is `GiftReceipt.fromAmount(1).constructor.name`: a derived class that does not
redeclare a factory inherits the base one, which returns the base type (`Receipt`, not
`GiftReceipt`) — Migration (d). `gift.d.ts` keeps the cross-module references:

```ts
export declare class GiftReceipt extends Receipt {
    protected static readonly byGiftReceiptAmount: unique symbol;
    protected static readonly byGiftReceiptCents: unique symbol;
    readonly note: string;
    constructor(overload: typeof GiftReceipt.byGiftReceiptAmount, amount: string);
    constructor(overload: typeof GiftReceipt.byGiftReceiptCents, cents: number);
    static fromGiftReceiptAmount(amount: string): GiftReceipt;
    static fromCents(cents: number): GiftReceipt;
}
```

- A derived class may read the base's protected static selector (`super(Money.byMoneyAmount, …)`);
  TS2445 scopes protected access to the class and its subclasses.
- Module evaluation order is safe for the `extends` chain: a derived module imports its base, so
  the base selectors exist before any derived constructor runs. Static initialization inside one
  class is R9's concern.

## R3 — A non-derived caller is rejected

Probe `s013r2/b/outsider.ts` [V] writes `new Money(Money.byMoneyAmount, 3)` in a module with no
class:

```text
outsider.ts(2,36): error TS2445: Property 'byMoneyAmount' is protected and only accessible within class 'Money' and its subclasses.
exit=1
```

Outsiders must use the factories. Reviewer probe `rev013/p3/r3.ts` [V] shows the limits: TS2445
is compile-time only, `Reflect.construct(Circle, [(Shape as any).byScale, 2])` still reaches the
selector branch, and `(Shape as unknown as { byScale: symbol }).byScale` reads it. Selectors are an
access convention, not a security boundary (spec § Divergences).

## R4 — Generic classes re-declare their constraints on factories

Probe `s013r2/d/box.ts` [V]: `class Box<T extends Comparable<T>>` with
`static fromValue<T extends Comparable<T>>(value: T): Box<T> { return new Box(Box.byBoxValue, value); }`
compiles, and Node prints `3 4` for `Box.fromValue(new N(3)).value.n` and
`Box.fromValueN(new N(4), 2).value.n`. Dropping the constraint on the factory
(`s013r2/d/box-noconstraint.ts`, `static fromValue<T>(value: T): Box2<T>`) fails:

```text
box-noconstraint.ts(7,39): error TS2344: Type 'T' does not satisfy the constraint '{ compareTo(o: T): number; }'.
box-noconstraint.ts(7,44): error TS2322: Type 'Box2<{ compareTo(o: T): number; }>' is not assignable to type 'Box2<T>'.
box-noconstraint.ts(7,78): error TS2345: Argument of type 'T' is not assignable to parameter of type '{ compareTo(o: T): number; }'.
```

- A static factory cannot use the class's type parameters, so it re-declares them, with the class
  constraints (FR-028).
- `typeof Box.byBoxValue` needs no instantiation; static members are shared by every
  instantiation.

## R5 — Interactions

Transpiled with `dotnet run --project src/Metano.Compiler.TypeScript/` against scratch projects
[V] (round 1 `glm25/r5`, round 2 `s013r2/r7`, review `rev013/p1`):

- **`this(...)` chains** [V]: `Temp(int a)` + `Temp() : this(42)` emits the dispatcher with the
  chain inlined into the zero-argument branch (`const __chain0Arg0 = 42; { const a = __chain0Arg0;
  … }`). Factories need no special case: the chaining overload's factory calls its own selector
  branch, which holds the inlined chain.
- **Record positional + secondary constructor** [V]: `record Rec(int X) { public Rec() : this(1) { } }`
  is already a dispatched set; the promoted `readonly x` and the `equals` / `hashCode` / `with`
  synthesis are untouched.
- **`[PlainObject]`** [V]: a secondary constructor on `P2(int A)` is dropped silently (the
  interface shape is unchanged, no diagnostic). No class exists, so factories have nothing to
  attach to (DECIDED-10, OQ-5).
- **`[InlineWrapper]` / branded** [V]: a second constructor on `readonly record struct W2(int V)` is
  dropped silently as well; the single-`create` shape stays.
- **Abstract dispatched constructors** [V]: a static factory doing `new Shape(Shape.byScale, scale)`
  inside `abstract class Shape` fails with `error TS2511: Cannot create an instance of an abstract
  class.` (round-1 probe). Selectors, selector signatures and a derived `super(Shape.byScale, …)`
  produce no diagnostic.
- **Abstract method overloads are skipped today** [V] (`s013r2/r7`, first run):

  ```text
  Extra.cs(38,23): error MS0001: Overload group 'Op.Run' contains constructs that the IR pipeline does not model yet; the methods were skipped.
  ```

  An `abstract` overload group has no body for the private fast path to hold. Root anchoring
  (DECIDED-15) gives it a shape: `abstract runX(...)` fast paths plus the concrete dispatcher (R8).
- **A struct's implicit parameterless constructor** [V] (`rev013/p1`, `s013r2/r7`): for
  `struct Pt { Pt(int x); Pt(int x, int y); }`, `new Pt()` emits `new Pt()` against a dispatcher
  with no zero-argument signature or branch:

  ```text
  out/scratch/user.ts(13,15): error TS2554: Expected 1-2 arguments, but got 0.
  ```

  At run time it would throw `No matching constructor`. FR-027 adds the branch (R11).
- **`[ObjectArgs]`, `[Emit]` / `[Import]` members with overloads, `[JsCallable]`** [I]: not probed —
  none has a dispatched class constructor or a Metano-generated overload body.

## R6 — Dart mapping (proposal, not probed)

The Dart target is a shape-only prototype with no emitter for these members [I]. The mapping rests
on Dart's language surface: named constructors (`Dimension.fromSize(...)`), no `unique symbol`
equivalent, and one member namespace per class shared by instance members, static members and
named constructors (which is why claims are resolved per target, DECIDED-18).

## R7 — Today's fast-path naming against the new policy (re-run on `f520be36`)

Scratch project `s013r2/r7` [V]: round 1's `Shapes` groups plus `Extra` (`U(int)` / `U(uint)`,
`W(long)` / `W(ulong)`, `L(List<int>)` / `L(int[])`, `P(params int[] parts)` / `P(string s)`),
transpiled on this branch (`IrToTsOverloadDispatcherBridge.ComputeFastPathNames`, `SimpleTypeName`,
`PrimitiveSuffix`; `IrToTsClassBridge.Operators.SimpleTypeName` for operators).

| C# group | Today (verbatim) | 013 policy |
|---|---|---|
| `Add(int x, int y)` / `Add(Point p)` | `addXY`, `addP` | unchanged (pass 1) |
| `F(int a)` / `F(string a)` | `fInt`, `fString` | `fInt32`, `fString` |
| `G(int v)` / `G(long v)` | `gInt`, `gLong` | `gInt32`, `gInt64` |
| `W(long a)` / `W(ulong a)` | `wLong`, `wULong` (#473) | `wInt64`, `wUInt64` |
| `U(int a)` / `U(uint a)` | `uInt`, `uInt` — **duplicate** | `uInt32`, `uUInt32` |
| `L(List<int> xs)` / `L(int[] xs)` | `lIntArray`, `lIntArray` — **duplicate** | `lListOfInt32`, `lInt32Array` |
| `Sum(int[] xs)` / `Sum(double[] xs)` | `sumIntArray`, `sumDoubleArray` | `sumInt32Array`, `sumDoubleArray` |
| `H(int? maybe)` / `H(string? maybe)` | `hIntOrNull`, `hStringOrNull` | `hInt32OrNull`, `hStringOrNull` |
| `K(Numerics.Decimal d)` / `K(Other.Decimal d)` | `kDecimal`, `kDecimal` — **duplicate** | `kNumericsDecimal`, `kOtherDecimal` |
| `Now()` / `Now(int tick)` | `now`, `nowTick` — `now` **is the dispatcher's name** | `nowDefault`, `nowTick` |
| `Format(string fmt, int count)` / `Format(string fmt, params object[] args)` | `formatFmtCount`, `formatFmtArgs` | unchanged (pass 1) |
| `Identity<T>(T value)` / `Identity(string value)` | `identityValue<T>`, `identityString` | `identityT<T>`, `identityString` |
| `P(params int[] parts)` / `P(string s)` | `pParts`, `pS` | unchanged (pass 1) |
| operators `+(Acct, long)` / `+(Acct, ulong)` | `__addAcctLong`, `__addAcctULong` (`Int64OverloadNamingTests`) | `__addAcctInt64`, `__addAcctUInt64` |

- `Identity`'s parameter names collide (`value` twice), so today's pass 2 runs and spells the type
  parameter with the `_ => "Value"` fallback; the 013 spelling of a type parameter is its name
  (`T`).
- `U` and `L` collide because the IR folds `uint` into `Int32` (`IrTypeRefMapper.cs:75-83`, #475)
  and lowers `List<int>` and `int[]` to the same array type. 013 spells from the Roslyn symbol
  captured at extraction (FR-002), so neither collision depends on #475.

`tsgo --strict --noEmit` over the generated package (`s013r2/r7/tsconfig.json`, with
`metano-runtime` and `decimal.js` resolved to the workspace) [V]:

```text
out/scratch/derived.ts(8,3): error TS2416: Property 'add' in type 'Derived' is not assignable to the same property in base type 'Base'.
out/scratch/ext.ts(4,17): error TS2323: Cannot redeclare exported variable 'twice'.
out/scratch/ext.ts(4,17): error TS2393: Duplicate function implementation.
out/scratch/ext.ts(8,17): error TS2323: Cannot redeclare exported variable 'twice'.
out/scratch/ext.ts(8,17): error TS2393: Duplicate function implementation.
out/scratch/extra.ts(6,18): error TS2393: Duplicate function implementation.
out/scratch/extra.ts(10,18): error TS2393: Duplicate function implementation.
out/scratch/extra.ts(50,18): error TS2393: Duplicate function implementation.
out/scratch/extra.ts(54,18): error TS2393: Duplicate function implementation.
out/scratch/shapes.ts(119,18): error TS2393: Duplicate function implementation.
out/scratch/shapes.ts(123,18): error TS2393: Duplicate function implementation.
out/scratch/shapes.ts(145,18): error TS2393: Duplicate function implementation.
out/scratch/shapes.ts(149,10): error TS2385: Overload signatures must all be public, private or protected.
out/scratch/shapes.ts(149,10): error TS2393: Duplicate function implementation.
out/scratch/user.ts(13,15): error TS2554: Expected 1-2 arguments, but got 0.
```

(Lines 150–151 repeat TS2385 / TS2393 for the other `now` signatures; three further errors are
unrelated to this feature and listed below.)

- `extra.ts` 6/10 are `uInt`, 50/54 `lIntArray`; `shapes.ts` 119/123 are `kDecimal`, 145–151 the
  private `now` beside the `now` dispatcher. The class path never runs `ClaimName`
  (`IrToTsOverloadDispatcherBridge.cs:257` is called only from `BuildNamespaceFunctions`, line
  137).
- `ext.ts`: overloaded extension methods emit two `export function twice` with no dispatcher
  (FR-032).
- `derived.ts`: R8.
- `user.ts`: the struct `new Pt()` of R5.
- Unrelated, recorded for a separate issue: `cash.ts(19,7) TS2322` (`this.a = cents` assigns an
  `int` to a `decimal` field without the implicit conversion) and `shapes.ts(120,14)` /
  `(124,14) TS2551` (the user structs `Numerics.Decimal` / `Other.Decimal` are imported as
  `decimal.js`'s `Decimal`). Round 1 called the latter a scratch artifact; it is a real
  type-resolution bug.

Constructor emission today [V]: `Dimension(int size)` / `Dimension(int width, int height)` emits one
constructor with dispatch-ordered overload signatures, a `(...args: unknown[])` body of guarded
branches each rebinding `const width = args[0] as number; …`, and no way to address one overload.
`Cash(decimal amount)` / `Cash(int cents)` beside `static From(decimal amount)` / `From(int cents)`
already emits private fast paths `fromAmount` / `fromCents` (`rev013/p1/out/p1/money.ts`), the names
the factories of FR-007 claim (FR-004, R11).

## R8 — Root-anchored fast paths: overrides, abstract groups, interfaces

**Today's bug** [V]: `Derived : Base` overriding `Add(int x)` only emits a bare `add(x)` that
replaces the whole dispatcher (`s013r2/r7/out/scratch/derived.ts`). Besides TS2416 (R7), it
misdispatches at run time (`s013r2/today`, Node with `--experimental-strip-types`, the generated
files with stubbed type checks):

```text
2 NaN
```

`d.add("abc")` returns `NaN`; C# returns `3`. `Derived.Twice` emits `super.add(x) + this.add(x)`.
An interface implementer names its fast paths after its own parameters (`Sq.areaSide` for
`Area(int side)`, the interface spells `Area(int s)`).

**Root-anchored shape** — probe `s013r2/a` [V]: `Base` declares `addX` / `addS` fast paths and the
dispatcher; `Derived` emits only `override addX(x)` and `twice(x) { return super.addX(x) +
this.addX(x); }`; `abstract class Op` declares `abstract runX` / `abstract runS` plus the concrete
dispatcher, `Neg` overrides both; `interface IShape` declares the dispatcher signatures plus
`areaS(s)` / `areaWH(w, h)`, and `Sq` implements them (as `areaS(side)` — the parameter name is the
implementer's, the member name the root's). Compiled with `--strict --noImplicitOverride
--declaration`: `exit=0`. Run:

```text
== node
2 2 3 15 8 -4 -2 9 10
== bun
2 2 3 15 8 -4 -2 9 10
```

In order: `b.addX(1)` on a `Derived` (direct call, virtual), `b.add(1)` (inherited dispatcher →
`this.addX`), `b.add("abc")` (the non-overridden overload, `3` as in C#), `twice(5)` (`super.addX`
+ `this.addX`), `b.addX.bind(b)(4)` (method group), `op.runX(4)`, `op.run("ab")`, `sh.areaS(3)`,
`sh.area(2, 5)`.

A hand-written 2.x implementer that has only the dispatcher no longer satisfies the interface
(`s013r2/a/handwritten.ts`) [V]:

```text
handwritten.ts(3,14): error TS2420: Class 'Tri' incorrectly implements interface 'IShape'.
  Type 'Tri' is missing the following properties from type 'IShape': areaS, areaWH
```

That is Migration (a).

## R9 — Static initialization order and an ESM cycle

Probe `s013r2/c` [V]: `class Money` with `static readonly Zero: Money = Money.fromCents(0)` (a C#
static field initialized through the factory), in three layouts, run under Node:

| Layout | Output |
|---|---|
| `Zero` before the selectors, selector branch `args[0] === Money.byMoneyAmount` | `naive-last: {"v":0,"how":"amount selector"}` — **wrong branch, silently**: both selectors are still `undefined`, so `undefined === undefined` matches the first one |
| `Zero` before the selectors, branch guarded by `typeof args[0] === "symbol" &&` | `throw new Error("No matching constructor");` — loud: the factory's `undefined` first argument falls through to the arity dispatcher, which rejects `(undefined, 0)` |
| selectors first, guarded | `first-guarded: {"v":0,"how":"cents selector"}` — correct |

`tsgo` accepts all three (`exit=0`): TS2729 does not see through the factory call. Hence FR-029
(selectors first among all static members and static blocks) **and** the guard of FR-009 (a
belt-and-braces check that turns any remaining ordering mistake into an error instead of a wrong
overload).

ESM cycle [V]: `a.ts` declares `static readonly Seed: B = B.fromX(1)`; `b.ts` imports `A` and uses
it only inside a method. Entry `a`: `entry a: 1 1`. Entry `b`:

```text
ReferenceError: Cannot access 'B' before initialization
```

The same program written with today's plain constructors (`static Seed = new B(1)`,
`s013r2/c/today`) fails identically under Node (`ReferenceError: Cannot access 'B' before
initialization`) and Bun (`ReferenceError: Cannot access '' before initialization.`). The cycle
hazard is the class binding's TDZ, which predates this feature; selectors add none, because they
are read only while a constructor runs, never while a module evaluates.

## R10 — Per-overload visibility, and `stripInternal`

Probe `s013r2/d/acct.ts` [V]: `Acct(int id)` public, `Acct(string code)` private,
`Acct(int id, string code)` protected, `Acct(bool flag)` internal; the constructor keeps the most
permissive (public) accessibility; the factories are `static fromId`, `private static fromCode`,
`protected static fromIdCode`, `/** @internal */ static fromFlag`; `static parse(code)` calls the
private factory from inside the class, and `Sub extends Acct` calls `Acct.fromIdCode(1, "x")`.
`exit=0`; Node prints `id code:q idcode flag …`. An outsider (`s013r2/d/outsider.ts`):

```text
outsider.ts(2,23): error TS2341: Property 'fromCode' is private and only accessible within class 'Acct'.
outsider.ts(3,23): error TS2445: Property 'fromIdCode' is protected and only accessible within class 'Acct' and its subclasses.
```

- Factories are separate static members, so each one carries its own overload's accessibility; the
  single-accessibility rule of TS2385 binds only the constructor's own overload signatures. Round
  1's DECIDED-8 was wrong on this point.
- `--stripInternal` over `acct.ts` keeps `static fromId(id: number): Acct;`, `private static
  fromCode;` and `protected static fromIdCode(...)`, and drops `fromFlag` (the `internal` overload)
  — the #445 meaning of the tag, unchanged (DECIDED-17).

Selectors must not follow the overload's accessibility (probe `s013r2/f`) [V]: a `private static
readonly byPCode: unique symbol` compiles, but the `.d.ts` drops its type —

```ts
    private static readonly byPCode;
    protected static readonly byPId: unique symbol;
    constructor(overload: typeof P.byPCode, code: string);
```

— so `typeof P.byPCode` is `any` for a consumer, and `new P(5, "x")` type-checks against that
`.d.ts` (`exit=0`). Protected selectors keep their `unique symbol` type in the `.d.ts` (R1), which
is why every selector stays `protected` (DECIDED-17, OQ-2 closed).

## R11 — Structs, `params`, synthesized claims, module groups

Probe `s013r2/d` [V], `exit=0`, Node output `… 0 4 3 5 150 7 200 6 6 3 4`:

- **Struct `new Pt()`** (`struct.ts`): the zero-argument signature `constructor();` plus a first
  branch `if (args.length === 0) { this.x = 0; return; }` beside the selector branches; `new
  Pt().x` → `0`, `Pt.fromX(4).x` → `4`, `Pt.fromXY(1, 2).x` → `3`, `new Pt(5).x` → `5`.
- **Synthesized claims** (`cash.ts`): factories `fromAmount` / `fromCents` claim their names
  before the `From` group's fast paths, which become `fromAmount$` / `fromCents$`;
  `Cash.fromAmount(1.5).v` → `150`, `Cash.fromCents$(7).v` → `7`, `Cash.from(2).v` → `200`.
- **`params`**: `static sumParts(...parts: number[])` called as `Cash.sumParts(1, 2, 3)` (C#
  expanded form) and `Cash.sumParts(...arr)` (C# normal form) → `6 6`. Today's dispatcher call
  site already spreads a normal-form array (`Extra.p(...[1, 2])`, `s013r2/r7/out/scratch/user.ts`).

Probe `s013r2/e` [V]: the overloaded extension `Twice(this Base b, int x)` / `Twice(this Base b,
string s)` as exported fast paths `twiceX` / `twiceS` plus an exported dispatcher `twice`; a
call site imports `twiceX` from the declaring module: `exit=0`, Node prints `4 3`. A namespace
barrel that re-exports a second module's `twiceX` (`index.ts`):

```text
index.ts(2,17): error TS2300: Duplicate identifier 'twiceX'.
index.ts(3,10): error TS2300: Duplicate identifier 'twiceX'.
```

So module fast paths are claimed in the barrel scope, not per file (FR-032).

## R12 — Static binding versus today's runtime-type dispatch

Scratch project `s013r2/g` [V]: `F(object a)` / `F(string a)` and `Run() { object o = "s"; return F(o); }`.
Today's output calls the dispatcher (`return Fs.f(o);`), whose first branch tests
`typeof args[0] === "object"` (false for a string) and whose second tests `isString`, so it runs
`fString` and returns `"string"`. C# binds `F(object)` statically and returns `"object"`. A direct
call site emits `Fs.fObject(o)` and matches C# — Migration (b). (The `object` check itself,
`typeof … === "object"`, rejects every primitive C# boxes as `object`; that dispatcher limitation
predates this feature and only hand-written callers keep facing it.)

## R13 — Remaining prescribed shapes

Probe `s013r2/h` [V], compiled with `--strict --noImplicitOverride --declaration` (`exit=0`), Node
and Bun both print `2 3 100 3 2 6 3 5 4`:

- **A derived type adding an overload** (FR-030): `Derived` overrides `addX`, adds `addBoolean`,
  and redeclares the dispatcher `add` with all three signatures (a superset of `Base.add`'s, so no
  TS2416); `d.add(1)` → `2`, `d.add("abc")` → `3`, `d.add(true)` → `100`.
- **One method implementing two roots** (naming.md § Root anchoring): `Both implements IA, IB`
  holds the body in `fA` and forwards `fB(b) { return this.fA(b); }` → `3`.
- **Operator fast paths** (FR-033): public `__addMoneyInt32` / `__addMoneyMoney` beside the `__add`
  dispatcher; the direct call `Money.__addMoneyInt32(new Money(1), 5).v` → `6`.
- **Explicit defaults in `super`** (FR-017): `Q(int c) : base(1)` against `P(int a, int b = 5)`
  emits `super(P.byPAB, 1, 5)` → `b` is `5`.
- **JSON factory callee** (FR-035): `factory: (p: Record<string, unknown>) => P.fromAB(p.a as number, p.b as number)` → `4`.
