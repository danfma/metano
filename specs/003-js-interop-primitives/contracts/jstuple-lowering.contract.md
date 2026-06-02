# Contract: `[JsTuple]` Lowering

**Covers**: FR-006, FR-007, FR-008, FR-009, FR-010 (User Story 2)

## T1 — Standalone `[JsTuple]` record → tuple type alias

**In**:
```csharp
[JsTuple] public record Pair<A, B>(A First, B Second);
```
**Out** (`pair.ts`):
```ts
export type Pair<A, B> = [A, B];
```
- No class, no `equals`/`hashCode`/`with`, no constructor. [FR-007]
- Element order = positional declaration order. [FR-006]

## T2 — Positional member access → array index

**In**: `var p = makePair(); ... p.First ... p.Second`
**Out**: `... p[0] ... p[1]` [FR-008]

## T3 — `[JsTuple, Import]` → erased, resolves to imported type

**In**:
```csharp
[JsTuple, Import("Signal", from: "solid-js")]
public record Signal<T>(Func<T> Getter, ISignalSetter<T> Setter);

private Signal<int> _counter; // field
```
**Out**: no `Signal` type emitted; the field annotation resolves to `Signal<number>` imported from `"solid-js"`. [FR-009]

## T4 — Never constructed in output

A `[JsTuple]` value is produced by a JS factory (an `[Import]`/`[Emit]` function), so `new Signal(...)` does not appear in transpiled code; the factory call returns the array. [FR-009]

## T5 — Misuse → MS0027

**In**: `[JsTuple] public record Bad { public int X { get; init; } }` (non-positional)
**Out**: diagnostic `MS0027 InvalidJsTuple` at the type declaration; no silently-wrong output. [FR-010]

## Invariants
- `[JsTuple]` is the array-shape sibling of `[PlainObject]` (object-shape). Both are shape-only (no class body).
- TypeScript-specific; other targets treat it as a no-op.
