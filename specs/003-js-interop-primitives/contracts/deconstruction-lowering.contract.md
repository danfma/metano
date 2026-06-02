# Contract: Tuple Deconstruction Lowering

**Covers**: FR-011, FR-012, FR-013 (User Story 3)

## D1 — Basic deconstruction

**In**: `var (a, b) = makePair();`
**Out**: `const [a, b] = makePair();` [FR-011]

## D2 — Discard

**In**: `var (_, b) = makePair();`
**Out**: `const [, b] = makePair();` [FR-012]
- The discarded position is an empty hole; only `b` is bound.

## D3 — References resolve to destructured names

**In**:
```csharp
var (count, setCount) = createSignal(0);
return count();
```
**Out**:
```ts
const [count, setCount] = createSignal(0);
return count();
```
- Later uses reference `count`/`setCount`, not index access. [FR-013]

## D4 — `let` vs `const`

A mutable C# local (reassigned) lowers to `let [a, b] = …`; otherwise `const`. (Mirrors existing `IrVariableDeclaration.IsConst` behavior.)

## Out of scope (deferred)
- Nested deconstruction `var ((a, b), c) = …`.
- Assignment-deconstruction `(a, b) = expr;` (no `var`).
- `foreach (var (k, v) in …)`.
