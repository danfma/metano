# Contract: SolidJS Reactivity & Helper Lowering

**Covers**: FR-015…FR-020, FR-022, FR-023 (User Story 4 + library recognition)

The `ISignal`/`SignalWrapper` abstraction is a compile-time facade over the Solid signal tuple `[Accessor<T>, Setter<T>]`. No wrapper object survives in output. [FR-015]

## R1 — Signal creation

**In**: `var count = Solid.CreateSignal(0);`
**Out**: `const count = createSignal(0);` — import `{ createSignal }` from `"solid-js"`. [FR-015, FR-023]
- No `SignalWrapper` / `new` / intermediate appears. [FR-015]

## R2 — Signal read

**In**: `count.Value`
**Out**: `count[0]()` [FR-016]

## R3 — Signal write (direct form, per clarification)

**In**: `count.Set(count.Value - 1)`
**Out**: `count[1](count[0]() - 1)` [FR-016]

**In**: `count.Set(x => x + 1)`
**Out**: `count[1]((x) => x + 1)` [FR-016]
- Value form is NOT wrapped in `() => v`. [FR-016]

## R4 — Effect

**In**: `Solid.CreateEffect(() => Console.WriteLine(count.Value));`
**Out**: `createEffect(() => console.log(count[0]()));` — import `{ createEffect }` from `"solid-js"`. [FR-017, FR-023]

## R5 — `For` helper → `<For>`

**In**: `Solid.For(items, (item, index) => new Counter { Count = item })`
**Out**:
```tsx
<For each={items}>{(item, index) => <Counter count={item} />}</For>
```
- import `{ For }` from `"solid-js"`. [FR-018, FR-023]
- (Deferred refinement: Solid passes `index` as an accessor; v1 prototype ignores it.)

## R6 — Render entry

**In**: `Render(() => new CounterGroup(), container);`
**Out**: `render(() => <CounterGroup />, container);` — import `{ render }` from `"solid-js/web"`. [FR-019, FR-023]

## R7 — Imported renderable type (library-agnostic, SC-004)

**In**: a renderable type carrying `[Import("Route", from: "solid-router")]` used in a renderable position, e.g. `new Route { Path = "/" , Children = [...] }`
**Out**: `<Route path="/">…</Route>` — import `{ Route }` from `"solid-router"`. [FR-022, FR-023]
- Recognition does not hard-code any library; it keys off `RendersAsJsxElement` + `[Import]`. [FR-022]

## Invariants

- Out of scope: any automatic field-mutation reactivity. Only the explicit signal API lowers. [FR-020]
- All required imports are added and merged per the existing import-merge rules. [FR-023]
