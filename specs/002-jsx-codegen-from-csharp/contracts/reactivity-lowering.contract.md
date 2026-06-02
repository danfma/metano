# Contract: SolidJS Reactivity & Helper Lowering

**Covers**: FR-015…FR-020, FR-022, FR-023 (User Story 4 + library recognition)

The signal binding is a `[JsTuple]` `Signal<T>(Func<T> Getter, ISignalSetter<T> Setter)` whose setter is a `[JsCallable]` interface — both erased compile-time facades over the Solid signal tuple `[Accessor<T>, Setter<T>]`. No wrapper object or declaration survives in output. Realized on the feature-003 `[JsTuple]`/`[JsCallable]`/tuple-deconstruction primitives. [FR-015]

## R1 — Signal creation (destructured)

**In**: `var (count, setCount) = Solid.CreateSignal(0);`
**Out**: `const [count, setCount] = createSignal(0);` — import `{ createSignal }` from `"solid-js"`. [FR-015, FR-023]
- No `Signal` / `ISignalSetter` / `new` / wrapper intermediate appears; the types are erased. [FR-015]

## R2 — Signal read

**In**: `count()` (the getter `Func<T>` invocation)
**Out**: `count()` [FR-016]

## R3 — Signal write (direct form via `[JsCallable]`)

**In**: `setCount.Invoke(count() - 1)`
**Out**: `setCount(count() - 1)` [FR-016]

**In**: `setCount.Invoke(x => x + 1)`
**Out**: `setCount((x) => x + 1)` [FR-016]
- Value form is NOT wrapped in `() => v`; both `Invoke` overloads lower to a direct call. [FR-016]
- No `count[0]()`/`count[1]()` index form, no `[Emit]` template. [FR-016]

## R4 — Effect

**In**: `Solid.CreateEffect(() => Console.WriteLine(count()));`
**Out**: `createEffect(() => console.log(count()));` — import `{ createEffect }` from `"solid-js"`. [FR-017, FR-023]

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
