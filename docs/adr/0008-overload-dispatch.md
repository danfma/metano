# ADR-0008 — Overload dispatch: slow-path dispatcher + fast-path private methods

**Status:** Accepted
**Date:** 2026-03-08

## Context

C# supports method and constructor overloading by signature. TypeScript
has a single runtime function per name — overloads at the type level are
declaration-only, and the runtime body is a single function that has to
figure out which overload was called.

Two translation strategies are canonical:

1. **Name mangling** at emit time. `Add(int, int)` becomes `addIntInt`,
   `Add(Point)` becomes `addPoint`. Consumers call the mangled name.
   Side-steps runtime dispatch entirely.
2. **Single runtime function with overload signatures** plus a
   `...args: unknown[]` body that inspects arg types at runtime and
   branches to the right body.

Name mangling forces consumers of the generated TS to know the
compiler's mangling scheme, and breaks silently whenever the user adds,
removes, or reorders an overload. It also diverges from the C# call
site — a consumer porting code from C# can no longer write
`thing.Add(something)` and rely on the compiler to pick.

A single-function dispatcher preserves the API but historically pays two
kinds of cost: the runtime type-inspection overhead on every call, and
the code-size overhead of inlining every overload body inside the
dispatcher (duplicated when the body is also reachable from elsewhere).

## Decision

Generate a single runtime dispatcher method for each overload group
(declared overload signatures + a body), **plus** one **private
fast-path method per overload** holding the actual body. The dispatcher
runs runtime type checks against `...args: unknown[]` and delegates to
the correct fast-path method — it never holds overload bodies inline.

```ts
class Vector {
  add(x: number, y: number): Vector;
  add(other: Point): Vector;
  add(...args: unknown[]): Vector {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1]))
      return this.addXY(args[0], args[1]);
    if (args.length === 1 && args[0] instanceof Point)
      return this.addPoint(args[0]);
    throw new TypeError(`Vector.add: no overload matches (${args})`);
  }

  private addXY(x: number, y: number): Vector { /* overload body 1 */ }
  private addPoint(p: Point): Vector { /* overload body 2 */ }
}
```

Runtime type checks are specialized by lowered type: `isInt32`,
`isString`, `typeof === "number"` for branded wrappers, `instanceof` for
classes, structural predicates for interfaces (see
[ADR-0009](0009-type-guards.md)). Void-returning overloads rewrite
`return expr;` inside the body to `expr; return;` so the dispatcher can
unify to `unknown` without losing the early-return semantics. Overloads
with different return types still work because the dispatcher is typed
at the declaration-signature level; each fast-path retains its specific
return type in its private declaration.

Fast-path naming uses the base method name + the capitalized parameter
names: `Add(int x, int y)` → `addXY`, `Add(Point p)` → `addPoint`,
`Add(string s, Priority p)` → `addStringPriority`. Conflicts (two
overloads with the same parameter names but different types) fall back
to appending the primitive type as a suffix.

Methods with a single overload emit no dispatcher — backward-compatible
with the pre-dispatch code path.

## Consequences

- (+) Public API matches C# exactly. A consumer ports `thing.Add(x, y)`
  and `thing.Add(point)` and both work without knowing how the
  dispatcher is wired.
- (+) Each overload's body lives in exactly one place — its fast-path
  method. Diffs stay clean when an overload's body changes; the
  dispatcher doesn't need to be re-emitted.
- (+) The fast-path pattern unlocks a future optimization:
  compile-time-known call sites (where the type of every argument is
  statically resolvable) can target the fast-path directly, skipping
  the dispatcher. That work is tracked as
  [issue #25](https://github.com/danfma/metano/issues/25) but requires
  no rewrite — the methods are already there.
- (+) Constructor overloads use the same pattern. No special-casing.
- (−) Runtime dispatch cost on every overloaded call. Each call pays
  for the type checks before reaching the body. For hot loops this
  matters; consumers who need maximum throughput can refactor to use
  the non-overloaded fast-path methods directly (they're `private` by
  convention but accessible via `(obj as any).addXY(...)`).
- (−) TS type inference at call sites still has to go through the
  declared overload signatures, not the dispatcher body. This is fine
  for consumers (they see the intended types), but the dispatcher's
  own return type is `unknown`, so any reflection over the method
  signatures sees the wide type.
- (−) Fast-path naming is heuristic. Conflicts require the
  primitive-type suffix fallback, which can produce names like
  `addStringInt32` that look less pretty than a pure rename.

## Alternatives considered

- **Name mangling as the public API** (`addXY`, `addPoint` exposed):
  rejected — breaks C# call-site parity, consumers have to learn the
  mangling rule, and refactors silently change the public name.
- **Inline all overload bodies directly inside the dispatcher**:
  rejected. Was the first iteration; produced duplicated bodies when
  an overload body was also reachable via a direct call elsewhere, and
  made diffs hard to review.
- **Emit overloads as separate top-level functions and dispatch at
  each call site**: rejected. Would force the consumer to import N
  functions per overload group and lose the method-syntax feel.

## References

- `src/Metano.Compiler.TypeScript/Transformation/OverloadDispatcherBuilder.cs`
- `src/Metano.Compiler.TypeScript/Transformation/TypeCheckGenerator.cs`
- `src/Metano.Compiler.TypeScript/TypeScript/AST/TsConstructorOverload.cs`
- `tests/Metano.Tests/MethodOverloadTests.cs`,
  `ConstructorOverloadTests.cs`, `MethodOverloadFastPathTests.cs`
- Related: [ADR-0009](0009-type-guards.md) for the runtime type check
  functions the dispatcher uses; static-known fast-path optimization
  and the other overload follow-ups (constructor factories, inheritance
  with overloads) are tracked as
  [issue #25](https://github.com/danfma/metano/issues/25).
