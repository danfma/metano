# Research: Overload factories, selector symbols and direct call sites

Evidence tags: **[V]** = verified on 2026-11-04, either by reading the cited file on this branch
(`5c084f50`, same tree as `main` plus no code changes) or by running the cited probe; **[I]** =
inferred from code or docs, not executed; **[M]** = maintainer decision.

Probe environment [V]: macOS arm64, .NET SDK 10.0.401, `tsgo` 7.0.0-dev.20260707.2 (the pinned
preview in `targets/js/metano-runtime`), Node (system), Bun 1.4.x. The probe programs live in the
session scratchpad and are not committed; every output line below is quoted verbatim.

## R1 — TypeScript accepts `typeof T.byXxx` in a constructor overload signature

Probe [V]: `class Money` with two `protected static readonly …: unique symbol` fields
(`Symbol("Money(decimal amount)")`, `Symbol("Money(int cents)")`), two constructor overload
signatures taking `typeof Money.byDecimal` / `typeof Money.byCents` first, a `(...args: unknown[])`
implementation dispatching on `args[0] === Money.byDecimal`, and two static factories. Compiled
with `tsgo --strict --declaration --target es2022 --module nodenext`:

```text
exit=0
```

The emitted `money.d.ts` [V]:

```ts
export declare class Money {
    protected static readonly byDecimal: unique symbol;
    protected static readonly byCents: unique symbol;
    readonly amount: number;
    readonly cents: number;
    constructor(overload: typeof Money.byDecimal, amount: number);
    constructor(overload: typeof Money.byCents, cents: number);
    static fromDecimal(amount: number): Money;
    static fromCents(cents: number): Money;
}
```

- `protected` **works**: the reference `typeof Money.byDecimal` is legal because the overload
  signature sits inside the class body; the `.d.ts` preserves the protected symbols, the selector
  signatures and the factories verbatim, so consumers see the full contract.
- The same file passes with `--isolatedModules --verbatimModuleSyntax --module esnext
  --moduleResolution bundler` (`exit=0`) [V] — the package's ESM build flags are safe.
- Runtime [V]: the factories run and dispatch correctly under Node.

No fallback alternative was needed. Had `protected` failed, the alternative was a module-private
symbol plus an exported type alias — not probed further because it is not required.

## R2 — A subclass in another module calls `super(Base.byXxx, ...)`

Probe [V]: `base.ts` exports the `Money` of R1; `derived.ts` imports it and declares
`class Receipt extends Money` whose implementation dispatches its own arguments and calls
`super(Money.byDecimal, …)` / `super(Money.byCents, …)`; `main.ts` imports both and uses only the
public factories. `tsgo --strict --declaration`:

```text
exit=0
```

The emitted `derived.d.ts` [V] keeps the cross-module selector references:

```ts
import { Money } from "./base.js";
export declare class Receipt extends Money {
    readonly label: string;
    constructor(overload: typeof Money.byDecimal, amount: number, label: string);
    constructor(overload: typeof Money.byCents, cents: number, label: string);
    static fromLabeledDecimal(amount: number, label: string): Receipt;
}
```

Run under Node and Bun (the repo lesson: verify load order under Node, since Bun hides some TDZ
issues):

```text
=== R2 node ===
990 2.5 coffee
=== R2 bun ===
990 2.5 coffee
```

- Accessing the base's `protected static` symbol from inside the derived class is allowed (TS2445
  scopes protected access to the class *and its subclasses*).
- `super(...)` before `this` access on both branches type-checks (TS proves the super call happens
  on every path).
- Module evaluation order is safe: `derived.ts` imports `base.ts` first, so the selector is
  initialized before the derived constructor can run.

## R3 — A non-derived caller is rejected

Probe [V]: `caller.ts` (a module with no class) writes `new Money(Money.byDecimal, 3)`:

```text
caller.ts(5,27): error TS2445: Property 'byDecimal' is protected and only accessible within class 'Money' and its subclasses.
exit=1
```

Exactly the intended access level: outsiders must use the factories; only subclasses may pass
selectors. `typeof T.byXxx` stays nameable in their types (the `.d.ts` of R1), so the signatures
still document themselves.

## R4 — Generic classes

Probe [V]: `class Box<T>` with two selector-guarded overloads and factories carrying their own
type parameters (`static fromValue<V>(value: V): Box<V> { return new Box(Box.byValue, value); }`).
`tsgo --strict --declaration`:

```text
exit=0
```

Runtime under Node:

```text
42 b
```

- `typeof Box.byValue` needs no instantiation — the static selector is addressed on the generic
  class as-is.
- A static factory declares its own `<V>`; the contextual type `Box<V>` drives the constructor
  overload resolution, and inference flows through the selector argument without annotation.
- Static members of a generic class are shared across instantiations, so the selector symbols are
  emitted once — no per-instantiation duplication to worry about.

## R5 — Interactions

Transpiled with `dotnet run --project src/Metano.Compiler.TypeScript/` on this branch against a
scratch project [V]:

- **`this(...)` chains** [V]: `Temp(int a)` + `Temp() : this(42)` emits the dispatcher with the
  chain inlined into the zero-arg branch (`const __chain0Arg0 = 42; { const a = __chain0Arg0; … }`)
  — `LowerThisChain`'s existing lowering. Factories need no special case: the chaining overload's
  factory calls its own selector branch, which contains the inlined chain.
- **Record positional + secondary constructor** [V]: `record Rec(int X) { public Rec() : this(1) { } }`
  is already a dispatched set; the emitted class keeps the promoted `readonly x` and the
  `equals` / `hashCode` / `with` synthesis untouched. The primary constructor is an ordinary member
  of the set (FR-014's basis).
- **`[PlainObject]`** [V]: `P1(int A)` emits `export interface P1 { readonly a: number; }`.
  `P2(int A)` with a secondary `P2(string s)` emits the same interface shape — **the secondary
  constructor is dropped silently**, with no diagnostic in the transpiler output. No class exists,
  so factories have nothing to attach to (DECIDED-10, OQ-5).
- **`[InlineWrapper]` / branded** [V]: `readonly record struct W(int V)` emits
  `export type W = number & { readonly __brand: "W" };` plus `W.create(value)`. A branded struct
  with a second constructor (`W2(int V)` + `W2(string s)`) emits the same single-`create` shape —
  **the second constructor is dropped silently** as well. Same conclusion as `[PlainObject]`.
- **Abstract classes** [V]: a TS probe with an `abstract class Shape` whose static factory does
  `new Shape(Shape.byScale, scale)`:

  ```text
  shape.ts(21,12): error TS2511: Cannot create an instance of an abstract class.
  exit=1
  ```

  TS2511 fires even inside the class's own static method, so abstract dispatched classes cannot
  carry factories that construct `T` (DECIDED-9). The selector machinery of R1/R2 works unchanged
  for them (the probe's selectors, overload signatures and the derived `super(Shape.byScale, …)`
  produced no diagnostic).
- **`[ObjectArgs]`, `[Emit]` / `[Import]` members with overloads, `[JsCallable]`** [I]: no probe —
  none has a dispatched class constructor to attach factories to (`[ObjectArgs]` synthesizes a
  `create` factory of its own; `[Emit]` / `[Import]` members have no Metano-generated body). They
  are excluded by FR-013 / DECIDED-11 and are not probed further.
- **Derived classes calling `super(...)` through today's dispatcher** [V by test]: the 2026-05-02
  spike closed sub-item 2 with tests (`DerivedWithOverloads_NonOverloadedBase_EachBranchCallsSuper`,
  `DerivedWithOverloads_OverloadedBase_SuperReachesBaseDispatcher`,
  `DerivedWithOverloads_RecordBase_SuperHonorsPrimaryCtorShape`, commit `d5e2f6d`); the base public
  constructor is the dispatcher, so `super(...)` reaches it. Selector-based supers (R2) replace the
  arg-based ones.

## R6 — Dart mapping (proposal, not probed)

The Dart target is a shape-only prototype; there is no Dart emitter for these members to probe
[I]. The mapping in DECIDED-14 rests on Dart's language surface: named constructors
(`Dimension.fromSize(...)`), no user-defined static-symbol machinery, no `unique symbol`
equivalent. Method fast paths reuse the same core names (DECIDED-1), which is exactly why the
policy must live in the target-agnostic core. Recorded as a proposal; the emission lands with
Dart parity (roadmap RT-02).

## R7 — Today's `ComputeFastPathNames` output vs the new algorithm

Scratch project [V] with method overload groups exercising every naming branch, transpiled on this
branch (`IrToTsOverloadDispatcherBridge.ComputeFastPathNames`, `SimpleTypeName`, `PrimitiveSuffix`):

| C# group | Today's fast paths (verbatim from the transpiled output) | New algorithm (013 FR-001) |
|---|---|---|
| `Add(int x, int y)` / `Add(Point p)` | `addXY`, `addP` | unchanged (parameter names, no joiner — DECIDED-2 holds) |
| `F(int a)` / `F(string a)` | `fInt`, `fString` | `fInt32`, `fString` (DECIDED-3 retires `Int`) |
| `G(int v)` / `G(long v)` | `gInt`, `gLong` | `gInt32`, `gInt64` |
| `Sum(int[] xs)` / `Sum(double[] xs)` | `sumIntArray`, `sumDoubleArray` | unchanged shape (`Array` suffix stands; element spellings per DECIDED-3) |
| `H(int? maybe)` / `H(string? maybe)` | `hIntOrNull`, `hStringOrNull` | unchanged shape (`OrNull` suffix stands) |
| `K(Numerics.Decimal d)` / `K(Other.Decimal d)` | `kDecimal`, `kDecimal` — **duplicate member** | `kNumericsDecimal`, `kOtherDecimal` (DECIDED-4 climb) |
| `Now()` / `Now(int tick)` | `now`, `nowTick` — `now` **is the dispatcher's own name** | `nowDefault`, `nowTick` (DECIDED-5) |
| `Format(string fmt, int count)` / `Format(string fmt, params object[] args)` | `formatFmtCount`, `formatFmtArgs` | unchanged (parameter names) |
| `Identity<T>(T value)` / `Identity(string value)` | `identityValue<T>`, `identityString` | unchanged (parameter names; type parameters propagate) |

Two of today's outputs do not compile. `tsgo --strict --noEmit` over the generated `shapes.ts`
(the `decimal.js` import error is a scratch-environment artifact; the class errors are not):

```text
shapes.ts(119,18): error TS2393: Duplicate function implementation.
shapes.ts(123,18): error TS2393: Duplicate function implementation.
shapes.ts(145,18): error TS2393: Duplicate function implementation.
shapes.ts(149,10): error TS2385: Overload signatures must all be public, private or protected.
shapes.ts(149,10): error TS2393: Duplicate function implementation.
shapes.ts(150,10): error TS2385: Overload signatures must all be public, private or protected.
shapes.ts(150,10): error TS2393: Duplicate function implementation.
shapes.ts(151,10): error TS2385: Overload signatures must all be public, private or protected.
shapes.ts(151,10): error TS2393: Duplicate function implementation.
```

- Lines 119/123 are the two `private static kDecimal` members: the type fallback
  (`SimpleTypeName` returns the bare `Name` of a named type) cannot split two types with the same
  simple name, and the class path never runs `ClaimName` (`IrToTsOverloadDispatcherBridge.cs:257`
  is called only from `BuildNamespaceFunctions`, line 137).
- Line 145 is `private static now()`: a zero-arg overload's parameter-name suffix is empty, so the
  fast path is the bare method name — the dispatcher's own name. TS reports the duplicate
  implementation and the mixed-visibility overload set (TS2385).

Both families are fixed by DECIDED-4 (climb) and DECIDED-5 (`Default`), which is why FR-004 makes
the class path run `ClaimName` too.

Constructor emission today [V] (the "before" the factories replace): `Dimension(int size)` /
`Dimension(int width, int height)` emits a single constructor with declaration-order-reversed
overload signatures (`constructor(width, height); constructor(size);` — dispatch order), a
`(...args: unknown[])` body of guarded branches each rebinding `const width = args[0] as number; …`,
and no way to address one overload. `Money(decimal amount)` / `Money(int cents)` /
`Money(int cents, bool rounded)` emits three branches with `isInt32` / `isBool` / `instanceof
Decimal` guards.
