# ADR-0028 — Overload factories, selector symbols and direct call sites

**Status:** Proposed (spec [013-overload-factories](../../specs/013-overload-factories/spec.md); implementation starts after #445)
**Date:** 2026-11-04
**Supersedes (in part):** [ADR-0008](0008-overload-dispatch.md)

## Context

ADR-0008 gave every overload group a public runtime dispatcher plus one **private** fast-path
method per overload. Four costs stayed open ([issue #25](https://github.com/danfma/metano/issues/25)):

1. Every **generated** call site pays the dispatcher's arity-and-type checks even when Roslyn
   resolved the overload statically, and the runtime choice can differ from C#'s static binding
   (`F(object)` / `F(string)` called with an `object` holding a string, research R12).
2. A **constructor overload has no TypeScript address**, so hand-written TypeScript cannot
   construct one overload and a derived class cannot pick a base overload except by argument shape.
3. The naming is a per-target heuristic over IR type refs that erase `uint`, `List<T>` and
   containing types, so several shape families emit duplicate members (research R7).
4. **Virtual overloads** misbehave: a derived class overriding one overload emits a bare method
   that replaces the whole dispatcher (TS2416; `d.add("abc")` returns `NaN`, research R8), and
   abstract overload groups are skipped (`MS0001`).

TypeScript's `unique symbol` closes the addressing gap: a `protected static readonly` symbol is a
runtime-comparable, type-level-distinct, subclass-visible name for one constructor overload
(research R1–R3).

## Decision

- **Candidates in the core, claims in the targets.** `Metano.Compiler` computes, per overload, a
  suffix and candidate names from Roslyn-captured type spellings: parameter names; exact CLR type
  names on any collision; then the colliding overloads climb their parameter types' containing
  types and namespaces; `Default` for the parameterless overload; `MS0003` plus positional markers
  at a dead end. Each target resolves the candidates against its own member tables in one fixed
  order, appending `$` per suffix and reporting `MS0067`.
- **Root-anchored names.** A method overload is named after its root declaration (the overridden
  chain's first declaration, or the interface member). Overrides emit `override <fastPath>`,
  abstract overloads `abstract <fastPath>`, interfaces declare the fast paths, and the inherited
  dispatcher keeps calling `this.<fastPath>`.
- **Constructor factories and type-qualified selectors.** A dispatched constructor emits, per
  overload, `protected static readonly by<TypeName><Suffix>: unique symbol` (first among the
  statics) and a static factory `from<Suffix>` with its overload's own accessibility (qualified as
  `from<TypeName><Suffix>` only when an inherited static would break the static side). The
  constructor tries guarded selector branches first and keeps today's arity-and-type branches as
  the fallback. Abstract classes get selectors but no factories.
- **Direct call sites.** Generated code that Roslyn resolved never reaches the dispatcher:
  constructors, `base(...)`, methods, `base.M`, method groups, operators, module functions and JSON
  factories target the factory, the selector or the fast path, when the declaring type's IR is
  available (source or project reference).
- **The dispatcher stays public** as the hand-written fallback.

## Consequences

- (+) Generated hot paths lose the runtime dispatch cost and bind like C#.
- (+) Hand-written TypeScript gains a typed construction API; `new Money(...)` keeps working.
- (+) Virtual and interface overloads dispatch correctly; abstract overload groups are supported.
- (+) The non-compiling naming families of research R7 / R8 compile, and every rename is derived
  from one documented algorithm or reported.
- (−) Generated names become public API (D7); renaming a parameter or a type renames them.
- (−) The `.d.ts` and barrels grow.
- (−) Hand-written subclasses overriding a generated dispatcher are bypassed; hand-written
  implementers of generated interfaces must add the fast paths.
- (−) `stripInternal` is unsupported for packages consumed by other Metano packages.
- (−) Packages and consumers upgrade together; metadata-only references keep dispatcher calls.

## Alternatives considered

- **Name mangling as the only API** (drop the dispatcher): breaks the hand-written TS contract.
- **Public factories only, no selectors**: a derived class could not pick a base overload, and
  the dispatcher could not delegate without type checks.
- **Factories that hold the bodies**: a derived constructor's branch would build a second instance.
- **Untyped selectors (`byAmount` on every class)**: TS2417 between a base and a derived static side.
- **Per-type fast-path names**: an override would emit a different name than the one a call
  through the base type selects.
- **Naming from the IR**: cannot separate `uint` / `int` or `List<int>` / `int[]`.

## References

- Spec: [specs/013-overload-factories/spec.md](../../specs/013-overload-factories/spec.md)
  (research R1–R13 holds the probe evidence).
- Superseded in part: [ADR-0008](0008-overload-dispatch.md) — its dispatcher contract stands; its
  private fast paths and heuristic naming do not.
- [ADR-0009](0009-type-guards.md), [ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md),
  baseline FR-049 / FR-050, #445, #475.
