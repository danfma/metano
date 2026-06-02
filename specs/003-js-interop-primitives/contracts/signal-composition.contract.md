# Contract: Signal Composition (the three primitives together)

**Covers**: FR-014 (User Story 4) — the motivating end-to-end win, validated with a self-contained inline binding (NOT the branch-002 SolidJS binding).

## S1 — Idiomatic destructured signal, zero `[Emit]`

**In** (inline test binding + usage):
```csharp
[JsTuple, Import("Signal", from: "solid-js")]
public record Signal<T>(Func<T> Getter, ISignalSetter<T> Setter);

[JsCallable, External] public interface ISignalSetter<T> {
    void Invoke(T value);
    void Invoke(Func<T, T> updater);
}

public static class Solid {
    [Import("createSignal", from: "solid-js")]
    public static Signal<T> CreateSignal<T>(T initial) => throw new NotSupportedException();
}

// transpiled body:
var (count, setCount) = Solid.CreateSignal(0);
Console.WriteLine(count());
setCount.Invoke(count() + 1);
setCount.Invoke(c => c + 1);
```

**Out**:
```ts
import { createSignal } from "solid-js";

const [count, setCount] = createSignal(0);
console.log(count());
setCount(count() + 1);
setCount(c => c + 1);
```

## Invariants (the whole point)
- **Zero `[Emit]` templates** in the binding. [FR-014]
- No `count[0]()` / `count[1]()` index forms.
- No `ISignal`/`Signal`/wrapper artifact in output — `Signal<T>` erased (resolves to Solid's `Signal`), `ISignalSetter<T>` erased.
- `count` is a `Func<T>` → `count()`; `setCount` is a `[JsCallable]` → `setCount(v)`.
- `import { createSignal } from "solid-js"` added.

## Note on the real binding
The actual `bindings/Metano.TypeScript.SolidJs` migration (replacing `ISignal.Value/.Set` with this `Signal<T>` shape) and the SolidJS consumer revalidation are the **dependent 002 reactivity refactor**, performed after `003` merges. This contract proves composition in isolation.
