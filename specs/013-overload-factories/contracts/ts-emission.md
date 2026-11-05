# Contract: TypeScript emission for overload factories, selectors and direct call sites

**Normative for**: 013 FR-007–FR-019, FR-021. Every emission row below is a golden (FR-021);
`fromXxx` / `byXxx` follow [naming.md](./naming.md). Probes: research R1–R5.

## A dispatched constructor (R1's shape, `Dimension(int size)` / `Dimension(int width, int height)`)

```ts
export class Dimension {
  protected static readonly bySize: unique symbol = Symbol("Dimension(int size)");
  protected static readonly byWidthHeight: unique symbol = Symbol("Dimension(int width, int height)");

  constructor(overload: typeof Dimension.bySize, size: number);
  constructor(overload: typeof Dimension.byWidthHeight, width: number, height: number);
  constructor(size: number);
  constructor(width: number, height: number);
  constructor(...args: unknown[]) {
    if (args[0] === Dimension.bySize) {
      const size = args[1] as number;
      this.size = size;
      return;
    }

    if (args[0] === Dimension.byWidthHeight) {
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
    return new Dimension(Dimension.bySize, size);
  }

  static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byWidthHeight, width, height);
  }
}
```

Rules:

- The selector fields are grouped with the other static fields (spec 005 member order), before the
  constructor; the factories sit with the static methods.
- The retained runtime overload signatures (here `constructor(size: number);` etc.) stay so a
  hand-written `new Dimension(3)` keeps type-checking (FR-010); the selector signatures are added
  ahead of them. R1 shows both forms coexist in `.d.ts`.
- The selector branch rebinds parameters from `args[1..]` (`const size = args[1] as number;`), as
  today's branches rebind from `args[0..]`; a `params` overload spreads
  `...(args.slice(n) as T[])`; an omitted optional argument the factory did not pass binds
  `undefined` (the parameter default applies).
- The fallback branches keep the guards of #438 / #440, the omitted-default rebinding, the ordering
  of `OverloadDispatchOrder`, and the `No matching constructor` message (FR-010).
- The symbol description is the full C# signature text, debug only (D2).

## Variants

| C# | Emission |
|---|---|
| A type with one constructor | unchanged — no selectors, no factories, no selector signatures (D1) |
| An abstract dispatched class | selectors + selector branches + selector signatures; **no factories** (FR-012, R5 TS2511); subclasses emit `super(Base.byXxx, ...)` |
| A `private` / `protected` constructor set (FR-049) | the factory carries the mapped modifier (`private static fromXxx`, `protected static fromXxx`); mixed sets emit the most permissive one on every factory and report `MS0060` as today; a widened set (MS0061) emits public factories |
| A `this(...)` chain (R5) | the chain stays inlined inside the target's selector branch; the chaining overload's factory is `static fromDefault(): Temp { return new Temp(Temp.byDefault); }` |
| A record positional constructor with a secondary constructor (R5) | every overload gets selector + factory; the primary's selector branch keeps the promoted-member assignments (FR-014) |
| `params int[] parts` overload | `static fromParts(...parts: number[]): T { return new T(T.byParts, ...parts); }` |
| `[PlainObject]`, `[InlineWrapper]` / `[Branded]`, `[External]`, `[NoEmit]`, `[JsCallable]` | unchanged — no selectors, no factories (FR-013, R5) |
| A class whose factory name a user member claims | `fromCents$` / `byCents$`… with `MS0067` (FR-004, FR-020) |

## A dispatched method group (R7's `Shapes`)

```ts
export class Shapes {
  /** @internal */
  static addXY(x: number, y: number): number {
    return x + y;
  }

  /** @internal */
  static addP(p: Point): number {
    return p.x + p.y;
  }

  static add(x: number, y: number): number;
  static add(p: Point): number;
  static add(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return Shapes.addXY(args[0] as number, args[1] as number);
    }

    if (args.length === 1 && args[0] instanceof Point) {
      return Shapes.addP(args[0] as Point);
    }

    throw new Error("No matching overload for add");
  }
}
```

- Fast paths are public with `/** @internal */` (FR-015); the naming is the policy's
  (`addXY`, `fInt32`, `nowDefault`, `kNumericsDecimal`).
- The dispatcher is unchanged: public, same signatures, guards, order, message (FR-016).
- Companion-module / `[ExportedAsModule]` fast paths become exported `/** @internal */` functions
  (`export function addXY(...)`); the dispatcher keeps the only overload signatures.

## Direct call sites (FR-017, FR-018)

| C# | TypeScript |
|---|---|
| `new Money(19.9m)` | `Money.fromAmount(19.9)` |
| `new Dimension(3, 4)` | `Dimension.fromWidthHeight(3, 4)` |
| `: base(19.9m)` in a derived constructor | `super(Money.byAmount, amount)` — the statically resolved base overload's selector (R2) |
| `: this(42)` | unchanged — the chain stays inlined in the target's selector branch |
| `Shapes.Add(1, 2)` (resolved to `Add(int, int)`) | `Shapes.addXY(1, 2)` |
| `p.Add(point)` (instance) | `p.addP(point)` — the receiver evaluated once in place |
| `this.Add(point)` inside the class | `this.addP(point)` |
| `Shapes.Identity<string>(s)` | `Shapes.identityString(s)`; a call resolved to the generic overload carries its type arguments (`Shapes.identityValue<T>(x)`) |
| a call on a `dynamic` / unresolved receiver, to a BCL mapping, an `[Emit]` / `[Import]` member, or with unresolved type arguments | today's dispatcher call (DECIDED-11) |
| `new T(...)` in a JSON `SerializerContext`'s `TypeSpec` for a dispatched type | `factory: (p) => T.fromXxx(...p)` (the `type:` line keeps the constructor reference; the `MS0061` widening applies to the factory too) |

## `.d.ts` surface (FR-021, R1's emitted file)

```ts
protected static readonly bySize: unique symbol;
constructor(overload: typeof Dimension.bySize, size: number);
static fromSize(size: number): Dimension;
/** @internal */
static addXY(x: number, y: number): number;
```

The selectors and the fast paths stay visible (removing them would break cross-package direct call
sites); `/** @internal */` documents that the name is the compiler's business (DECIDED-6).
