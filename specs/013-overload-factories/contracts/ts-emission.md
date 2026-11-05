# Contract: TypeScript emission for overload factories, selectors and direct call sites

**Normative for**: 013 FR-007–FR-019, FR-021, FR-025–FR-037. Every emission row below is a golden
(FR-021); names follow [naming.md](./naming.md). Probes: research R1–R15.

## A dispatched constructor (`Dimension(int size)` / `Dimension(int width, int height)`)

```ts
export class Dimension {
  protected static readonly byDimensionSize: unique symbol = Symbol("Dimension(int size)");
  protected static readonly byDimensionWidthHeight: unique symbol = Symbol("Dimension(int width, int height)");
  // ...then the other static fields and static blocks, in today's order (FR-029)

  size: number = 0;
  width: number = 0;
  height: number = 0;

  constructor(overload: typeof Dimension.byDimensionSize, size: number);
  constructor(overload: typeof Dimension.byDimensionWidthHeight, width: number, height: number);
  constructor(width: number, height: number);
  constructor(size: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Dimension.byDimensionSize) {
      const size = args[1] as number;
      this.size = size;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Dimension.byDimensionWidthHeight) {
      const width = args[1] as number;
      const height = args[2] as number;
      this.width = width;
      this.height = height;

      return;
    }

    // hand-written fallback — today's branches, unchanged
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) { … }

    if (args.length === 1 && isInt32(args[0])) { … }

    throw new Error("No matching constructor");
  }

  static fromSize(size: number): Dimension {
    return new Dimension(Dimension.byDimensionSize, size);
  }

  static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byDimensionWidthHeight, width, height);
  }
}
```

Rules:

- **Selectors first (FR-029).** The selector fields are the first static members of the class,
  before every other static field and static block, so a static initializer that calls a factory
  (`static readonly Zero = Money.fromCents(0)`) finds them initialized (R9).
- **Selector names (FR-008).** `by` + the declaring type's emitted name + the suffix; a derived
  class never redeclares a base selector (TS2417 otherwise, R2).
- **Guarded selector branches (FR-009).** Each selector branch tests `typeof args[0] === "symbol"
  && args[0] === T.by…`. An uninitialized selector (`undefined`) then falls through to the arity
  fallback, which throws, instead of matching the first selector branch (R9).
- **Retained plain signatures (FR-010).** The runtime overload signatures stay, after the selector
  signatures (R1), so `new Dimension(3)` keeps type-checking and dispatching by arity and type.
- **Rebinding.** A selector branch rebinds parameters from `args[1..]`. A `params` overload binds
  its rest as `args.slice(n) as T[]`. An optional parameter the factory omitted binds `undefined`,
  and the parameter default applies.
- **Fallback.** The fallback branches keep the guards of #438 / #440, the omitted-default
  rebinding, the `OverloadDispatchOrder` order (subject to #475, NFR-006) and the
  `No matching constructor` message.
- **Symbol description.** The full C# signature text, for debugging only (D2).

## Variants

| C# | Emission |
|---|---|
| A type with one declared constructor | unchanged — no selectors, no factories (D1) |
| Per-overload accessibility (FR-011) | each factory carries the #445 member mapping of **its** overload: `private static fromCode`, `protected static fromIdCode`, `/** @internal */ static fromFlag` (internal), `static fromX` (protected internal, untagged) (R10). The constructor keeps FR-049's most permissive modifier and `MS0060` |
| A factory constructed from outside the class body (a JSON `TypeSpec`, a `new` in a nested type) | that factory is emitted public and reports `MS0061`, as FR-049 rule 3 does for the constructor |
| An abstract dispatched class | selectors + selector signatures + selector branches; **no factories** (FR-012, TS2511) |
| A derived factory that keeps an inherited name (FR-026: same constructed parameter list, base factory not `private`, accessibility at least as wide) | `static override fromAmount(amount: Decimal): Receipt` — `override` is required under `noImplicitOverride` (TS4114, R14) |
| A derived class whose factory would break the static side (different list, `private` base factory, narrower accessibility) | `from` + type name + suffix, no `override` (FR-026; `GiftReceipt.fromGiftReceiptAmount` R2; `Receipt.fromReceiptCode` / `protected static fromReceiptId` R14) |
| A derived user static with an inherited factory's name | `MS0068` (error; DECIDED-26) |
| A struct with constructor overloads (FR-027) | adds `constructor();` and a first branch `if (args.length === 0) { <every instance field> = <default of its type>; return; }` — `null as never` for a reference type, the implicit zero of a number, `bool`, `decimal`, enum or nullable, `"\0"`, `0n`, `new S()` for a struct that construction zeroes; a value type with no zero value in the target (`Guid`, the date and time types) is left `undefined` and reported with `MS0001`; no factory, no selector for the implicit parameterless constructor; `new S()` keeps emitting `new S()` (R11). An **explicit** parameterless struct constructor is an ordinary overload (`fromDefault` / `bySDefault`) |
| A `this(...)` chain | the chain stays inlined in the target's selector branch; `static fromDefault(): Temp { return new Temp(Temp.byTempDefault); }` |
| A record positional constructor with a secondary constructor | every overload gets selector + factory; the primary's selector branch keeps the promoted-member assignments (FR-014) |
| `params int[] parts` overload | `static fromParts(...parts: number[]): T { return new T(T.byTParts, ...parts); }` |
| A generic class `Box<T> where T : IComparable<T>` | `static fromValue<T extends Comparable<T>>(value: T): Box<T>` — the factory re-declares the class type parameters **with their constraints** (FR-028, R4) |
| `[PlainObject]`, `[InlineWrapper]` / `[Branded]`, `[External]`, `[NoEmit]`, `[JsCallable]` | unchanged (FR-013) |
| A candidate claimed by another member | `fromCents$` + `byMoneyCents$` (per suffix) + `MS0067` (FR-004) |
| A dead-end collision | `…$1` / `…$2` + `MS0003` error (FR-024) |

## A dispatched method group, root-anchored (`Base` / `Derived`)

```ts
export class Base {
  addX(x: number): number {
    return x;
  }

  addS(s: string): number {
    return s.length;
  }

  add(x: number): number;
  add(s: string): number;
  add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addS(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }
}

export class Derived extends Base {
  override addX(x: number): number {
    return x * 2;
  }

  twice(x: number): number {
    return super.addX(x) + this.addX(x);
  }
}
```

- **Fast paths (FR-015).** Public for a public overload, with no JSDoc tag; every other overload
  keeps its own accessibility under #445's member mapping (`private`, `protected`, `internal` →
  public + `/** @internal */`, `protected internal` → public untagged, `private protected` →
  `protected` + `/** @internal */`). An override's fast path takes its **root's** mapped
  visibility (a cross-assembly `protected override` of a `protected internal` root is public;
  TS2415 otherwise, R14). The dispatcher keeps the group accessibility and #445's final
  mixed-group rule: only signatures no other package can reach — `internal`, `private protected`,
  or `private` inside a wider group — are tagged `/** @internal */`; a `protected` signature in a
  wider group is widened and left untagged.
- **Overrides (FR-030).** An override emits the root's fast path with `override`; a type that only
  overrides emits **no** dispatcher and inherits the root's, whose branches call `this.<fastPath>`
  and so dispatch virtually (R8). A type that declares new overloads emits an `override`
  dispatcher (TS4114 otherwise) whose signatures are a **superset of the base dispatcher's**,
  including base overloads the derived type cannot see (`private`, or `internal` to another
  assembly, tagged as #445 tags them); its branches for its own overloads call their fast paths,
  and every other call forwards to `super.<dispatcher>(...args)` (TS2416 otherwise, R14):

  ```ts
  export class Derived extends Base {         // Base: add(x: number) public, add(s: string) private
    addFlag(flag: boolean): number { … }
    override add(x: number): number;
    /** @internal */
    override add(s: string): number;
    override add(flag: boolean): number;
    override add(...args: unknown[]): number {
      if (args.length === 1 && isBoolean(args[0])) {
        return this.addFlag(args[0] as boolean);
      }

      return super.add(...(args as [never]));
    }
  }
  ```
- **Non-root candidates (claim step 7).** A `new` (hiding) method or a derived type's new overload
  whose candidate equals an inherited instance member of any visibility takes `$` (`addX$`,
  `addS$`, `MS0067` reason 3, R14); it never overrides a base fast path by accident.
- **Abstract groups (FR-030).** `abstract addX(x: number): number;` per abstract overload; the
  abstract class still emits the concrete dispatcher (today `MS0001`, R5).
- **`base.M(...)`** → `super.<fastPath>(...)`.

## Interfaces (FR-031)

```ts
export interface IShape {
  area(s: number): number;
  area(w: number, h: number): number;
  areaS(s: number): number;
  areaWH(w: number, h: number): number;
}

export class Sq implements IShape {
  areaS(side: number): number { … }      // the root's name, the implementer's parameter names
  areaWH(w: number, h: number): number { … }
  area(s: number): number;
  area(w: number, h: number): number;
  area(...args: unknown[]): number { … }  // today's dispatcher, calling this.areaS / this.areaWH
}
```

A hand-written implementer must now declare `areaS` / `areaWH` (TS2420, R8 — Migration (a)).
Default interface methods are out of scope (their bodies are dropped today).

### Explicit implementations (FR-037)

```ts
export class Plot implements IShape {      // int IShape.Area(int s); int IShape.Area(int w, int h); public int Area(bool big)
  areaS(s: number): number { … }           // explicit, under the root's name
  areaWH(w: number, h: number): number { … }
  areaBig(big: boolean): number { … }      // the class's own overload
  area(s: number): number;
  area(w: number, h: number): number;
  area(big: boolean): number;
  area(...args: unknown[]): number { … }   // covers the explicit and the class overloads
}
```

An explicit overload whose signature equals a class member's keeps today's `MS0031` (dropped).

### Multiply-constructed interfaces (FR-036)

```ts
export class C implements IConv<number>, IConv<string> {   // MS0069
  convX(x: number): number;
  convX(x: string): number;
  convX(...args: unknown[]): number { … }   // runtime dispatch over Conv(int x) / Conv(string x)
  convXN(x: number, n: number): number;
  convXN(x: string, n: number): number;
  convXN(...args: unknown[]): number { … }
  conv(...args: unknown[]): number { … }    // today's dispatcher
}
```

No per-implementation fast paths; direct calls into `C` use these dispatchers (R14).

In a derived type, the root-name dispatcher is `override`, carries every constructed signature and
forwards the constructions it does not implement to `super` (R15):

```ts
export class D extends C {                  // D : C, overriding Conv(int x)
  override convX(x: number): number;
  override convX(x: string): number;
  override convX(...args: unknown[]): number {
    if (isInt32(args[0])) {
      return 10;
    }

    return super.convX(...(args as [never]));
  }
}

export class D4 extends C1 implements IConv<string> {   // C1 : IConv<int>; D4 adds a construction, MS0069
  override convX(x: number): number;
  override convX(x: string): number;
  override convX(...args: unknown[]): number {
    if (isString(args[0])) {
      return 2;                               // D4.Conv(string): an implementation, not an override
    }

    return super.convX(args[0] as number);
  }
  override conv(x: number): number;
  override conv(x: string): number;
  override conv(...args: unknown[]): number { … }
}
```

## Module groups (FR-032)

Overloaded extension methods, `[ExportedAsModule]` functions and companion-namespace functions emit
one exported fast path per overload plus one exported dispatcher carrying the overload signatures:

```ts
export function twiceX(b: Base, x: number): number { … }
export function twiceS(b: Base, s: string): number { … }
export function twice(b: Base, x: number): number;
export function twice(b: Base, s: string): number;
export function twice(...args: unknown[]): number { … }
```

Today they emit two `export function twice` (TS2393, R7). The claim scope is the namespace barrel
(R11: two modules exporting `twiceX` through one `index.ts` is TS2300). The barrel re-exports the
fast paths beside the dispatcher.

## Operators (FR-033)

`__add` + type spellings from the same policy (`__addMoneyInt32`, `__addAcctUInt64`); the second
spelling table in `IrToTsClassBridge.Operators.cs` is deleted. Operator fast paths are public (C#
operators are always public static).

## Direct call sites (FR-017, FR-018, FR-035)

| C# | TypeScript |
|---|---|
| `new Money(19.9m)` | `Money.fromAmount(new Decimal("19.9"))` |
| `new Dimension(3, 4)` | `Dimension.fromWidthHeight(3, 4)` |
| `new Pt()` on a struct (implicit parameterless) | `new Pt()` (FR-027) |
| `: base(amount)` in a derived constructor | `super(Money.byMoneyAmount, amount)` in every branch that chains to that base overload |
| `: base(1)` to `Base(int a, int b = 5)` | `super(Base.byBaseAB, 1, 5)` — Roslyn's default-value arguments are passed explicitly |
| `: this(42)` | unchanged — inlined in the target's selector branch |
| `Shapes.Add(1, 2)` | `Shapes.addXY(1, 2)` |
| `s.Scale(2)` to `Scale(int f, int d = 10)` | `s.scaleFD(2, 10)` — the statically resolved overload's default passed explicitly (DECIDED-32) |
| `p.Add(point)` / `this.Add(point)` | `p.addP(point)` / `this.addP(point)` — the receiver evaluated once in place |
| `b.Add(1)` where `b` is `Base` and `Derived` overrides `Add(int)` | `b.addX(1)` (virtual, root name) |
| `sh.Area(2)` on an `IShape` | `sh.areaS(2)` |
| `base.Add(x)` | `super.addX(x)` |
| `Func<int, int> f = b.Add;` | `const f = b.addX.bind(b);` — the receiver binding is kept; a static method group is referenced unbound, as today |
| `b.Twice(3)` (extension) | `twiceX(b, 3)` with `twiceX` imported from the declaring module |
| `a + 5` resolved to `operator +(Money, int)` in an overloaded operator group | `Money.__addMoneyInt32(a, 5)` |
| `Extra.P(1, 2, 3)` (`params`, expanded form) | `Extra.pParts(1, 2, 3)` |
| `Extra.P(arr)` (`params`, normal form) | `Extra.pParts(...arr)` — the bound array spread into the rest parameter, as today's dispatcher call site does (R11) |
| `Shapes.Identity<string>(s)` resolved to the generic overload | `Shapes.identityT<string>(s)` — type arguments carried |
| a call on `dynamic`, an unresolved symbol, a BCL mapping, an `[Emit]` / `[Import]` member, unresolved type arguments, or a member of a metadata-only reference | today's dispatcher call (DECIDED-11, FR-019) |
| JSON `TypeSpec` of a dispatched type | `factory: (p: Record<string, unknown>) => Money.fromAmount(p.amount as Decimal)` — the same argument list as today's `new Money(...)`; only the callee changes; `Object.assign(Money.fromAmount(…), { … })` for init members (FR-035) |

## `.d.ts` surface

```ts
export declare class Money {
    protected static readonly byMoneyAmount: unique symbol;
    protected static readonly byMoneyCents: unique symbol;
    constructor(overload: typeof Money.byMoneyAmount, amount: Decimal);
    constructor(overload: typeof Money.byMoneyCents, cents: number);
    constructor(amount: Decimal);
    constructor(cents: number);
    static fromAmount(amount: Decimal): Money;
    static fromCents(cents: number): Money;
}
export declare class Base {
    addX(x: number): number;
    addS(s: string): number;
    add(x: number): number;
    add(s: string): number;
}
```

`@internal` appears only where #445 puts it (C# `internal`, `private protected`, a `private`
signature inside a wider dispatcher group; a `protected` one in a wider group is widened untagged);
a public overload's factory and fast path are untagged, so `stripInternal` keeps them
(R10, DECIDED-17).
