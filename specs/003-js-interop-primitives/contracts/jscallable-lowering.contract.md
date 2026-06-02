# Contract: `[JsCallable]` Lowering

**Covers**: FR-001, FR-002, FR-003, FR-004, FR-005 (User Story 1)

## C1 — Single-signature `Invoke` → direct call

**In**:
```csharp
[JsCallable, External] public interface IAction { void Invoke(int value); }
... cb.Invoke(5) ...
```
**Out**: `... cb(5) ...` [FR-001]
- No `.Invoke` member survives; no `[Emit]` authored.

## C2 — Overloaded `Invoke` (value + updater)

**In**:
```csharp
[JsCallable, External] public interface ISignalSetter<T> {
    void Invoke(T value);
    void Invoke(Func<T, T> updater);
}
... setCount.Invoke(5); setCount.Invoke(c => c + 1); ...
```
**Out**:
```ts
setCount(5);
setCount(c => c + 1);
```
- Both overloads lower to direct invocation; argument shape preserved. [FR-002]

## C3 — Arbitrary arity

**In**: `[JsCallable] interface I3 { void Invoke(int a, string b, bool c); } ... f.Invoke(1, "x", true)`
**Out**: `f(1, "x", true)` [FR-003]

## C4 — Erased interface

A `[JsCallable]` interface emits no declaration file/type; composes with `[Import]`/`[External]`. [FR-004]

## C4b — Type-position lowering (no dangling reference)

Because the interface is erased, a `[JsCallable]` type used as a parameter / field / return annotation cannot emit its name (there is no declaration or import for it). It lowers to its **inline call shape** instead:

- **Single `Invoke`** → a TS function type.

  **In**: `void Go(ISetter setCount)` where `[JsCallable] interface ISetter { void Invoke(int value); }`
  **Out**: `go(setCount: (value: number) => void): void`

- **Overloaded `Invoke`** → an intersection of call signatures.

  **In**: `void Go(ISignalSetter<int> setCount)` where `ISignalSetter<T>` has `Invoke(T)` and `Invoke(Func<T,T>)`
  **Out**: `go(setCount: ((value: number) => void) & ((updater: (arg: number) => number) => void)): void`

The emitted annotation is self-contained — no erased interface name survives, and no import of it is generated. [FR-004]

## C5 — Misuse → MS0028

| Input | Expected |
|-------|----------|
| `[JsCallable] class C { }` (non-interface) | `MS0028 InvalidJsCallable` at the type |
| `[JsCallable] interface I { void Invoke(int x); int Other(); }` (non-`Invoke` member) | `MS0028` at the offending member |

[FR-005]

## Invariants
- `Invoke` is the sole recognized call-operation member (mirrors delegate `.Invoke`).
- Replaces hand-written `[Emit("$0($1)")]` with a declarative marker; no template authored.
- TypeScript-specific; no-op for other targets.
