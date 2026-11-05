# ADR-0028 — Overload factories, selector symbols and direct call sites

**Status:** Accepted (spec [013-overload-factories](../../specs/013-overload-factories/spec.md); implementation starts after #445)
**Date:** 2026-11-04
**Supersedes (in part):** [ADR-0008](0008-overload-dispatch.md)

## Context

ADR-0008 gave every overload group a public runtime dispatcher plus one **private** fast-path
method per overload. Three costs stayed open, tracked as [issue #25](https://github.com/danfma/metano/issues/25):

1. Every **generated** call site pays the dispatcher's arity-and-type checks even when Roslyn
   resolved the overload statically (the optimization ADR-0008 explicitly deferred to this ADR).
2. A **constructor overload has no TypeScript address**: the dispatcher constructor inlines the
   bodies in branches, so hand-written TypeScript cannot construct one specific overload, and a
   derived class cannot pick a base overload except by argument shape.
3. The private fast-path **naming** is a per-target heuristic that emits non-compiling output for
   two shape families (research R7: a type-name fallback that collides into duplicate members,
   TS2393; a zero-arg overload whose fast path shadows the dispatcher's own name, TS2393 +
   TS2385).

TypeScript's `unique symbol` closes the addressing gap: a `protected static readonly byXxx:
unique symbol` is a runtime-comparable, type-level-distinct, subclass-visible name for one
constructor overload (verified under `tsgo --strict --declaration` and on Node and Bun — spec 013
research R1–R4).

## Decision

- **One naming policy, in the core.** `Metano.Compiler` computes, per overload of a group, one
  suffix (factory `from` + suffix, selector `by` + suffix) and the fast-path name: parameter
  names, exact C# type names on any collision, a containing-type / namespace climb on a remaining
  collision, `Default` for the parameterless overload, `$` (plus the new warning `MS0067`) when a
  user member claims the name. The targets only render.
- **Constructor factories and selector symbols.** A dispatched constructor emits, per overload, a
  static factory `fromXxx(...)` (accessibility = the constructor's emitted FR-049 modifier) and a
  `protected` selector symbol whose description is the C# signature. The constructor's
  implementation tries the selector branches first and keeps today's arity-and-type branches as
  the fallback; the factory constructs through the selector, so each body stays single-homed.
  Abstract classes get selectors but no factories (TS2511). Shapes without a dispatched class
  constructor (`[PlainObject]`, branded types) are unchanged.
- **Direct call sites.** Generated code that Roslyn resolved never reaches the dispatcher: `new
  T(args)` emits `T.fromXxx(args)`, `: base(args)` emits `super(Base.byXxx, ...)`, and a resolved
  method call emits the fast-path call. Dynamic, unresolved and BCL-mapped calls keep the
  dispatcher.
- **Public fast paths.** Method fast paths become public `/** @internal */` members (exported in
  companion modules) so cross-file and cross-package generated code can call them; factories are
  documented public API. The dispatcher stays public and byte-compatible as the hand-written
  fallback.

## Consequences

- (+) Generated hot paths lose the runtime dispatch cost (ADR-0008's deferred optimization).
- (+) Hand-written TypeScript gains a typed construction API (`Money.fromCents(990)`) while
  keeping `new Money(990)`.
- (+) Derived classes address the exact base overload (`super(Base.byXxx, ...)`).
- (+) The two non-compiling naming families of R7 are fixed, and every rename is either derived
  from one documented algorithm or reported (`MS0067`).
- (−) The emitted names become public API: renaming a C# parameter, moving a type, or adding an
  overload can rename them (documented; `[Name]` pins).
- (−) The `.d.ts` surface grows (selectors, selector signatures, factories, fast paths).
- (−) Packages and consumers must upgrade together: a 3.0 consumer emits factory / fast-path
  calls into 2.x-built packages that lack them.

## Alternatives considered

- **Name mangling as the only API** (drop the dispatcher; consumers must call `fromXxx`):
  rejected — breaks the hand-written TS contract ADR-0008 guarantees and forces a migration on
  every consumer of every overload, where this design breaks only the `(obj as any)` hacks.
- **Public factories only, no selectors**: rejected — without a runtime-comparable overload
  identity the dispatcher cannot delegate without type checks, and a derived class cannot pick a
  base overload.
- **Factories that hold the bodies directly** (no selector branch): rejected — a derived
  constructor's fallback branch would construct a second instance instead of running `super`.
- **Keep fast paths `private`, call them through `(obj as any)`:** rejected — loses every type
  check at rewritten call sites.
- **Target-local naming rules** (keep `ComputeFastPathNames` in the TS bridge): rejected — the
  direct-call rewrite needs the same names in `IrExpressionExtractor`, and Dart needs them for its
  named constructors; two copies diverge.

## References

- Spec: [specs/013-overload-factories/spec.md](../../specs/013-overload-factories/spec.md)
  (research R1–R7 holds the probe evidence).
- Superseded in part: [ADR-0008](0008-overload-dispatch.md) — its dispatcher contract stands; its
  private fast paths and heuristic naming do not.
- [ADR-0009](0009-type-guards.md) (the runtime checks the fallback keeps using),
  [ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md) (the shared IR hosts the
  policy), FR-049 / #432 (the constructor accessibility the factories inherit), #445 (the
  visibility bridge this builds on).
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsOverloadDispatcherBridge.cs`,
  `IrToTsConstructorDispatcherBridge.cs`, `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs`.
