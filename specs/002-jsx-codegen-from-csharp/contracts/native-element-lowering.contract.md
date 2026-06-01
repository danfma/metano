# Contract: Native Elements & Component Composition → JSX

**Covers**: FR-007…FR-014 (User Story 2 + User Story 3)

## N1 — Native element with attribute (camelCase + `[Name]` override)

**In**: `new Html.Div { ClassName = "counter", Id = "c1" }`
**Out**: `<div class="counter" id="c1" />`
- `ClassName` → `class` because the SolidJS binding declares `[Name("class")]`. [FR-008]
- `Id` → `id` by default camelCase (no override needed). [FR-008]
- No children ⇒ self-closing. [FR-010]

## N2 — String literal vs expression attribute; event handler

**In**:
```csharp
new Html.Button { ClassName = "action", OnClick = decrement }
```
**Out**: `<button class="action" onClick={decrement} />`
- String literal → `attr="…"`. [FR-009]
- Non-literal expression (handler reference) → `attr={…}`. `OnClick` camelCases to `onClick`. [FR-009]

## N3 — Children, text, nested elements

**In**:
```csharp
new Html.Div {
    Children = [
        new Html.Button { Children = [Text("-")] },
        new Html.Span { Children = [Text(count.Value)] },
    ],
}
```
**Out**:
```tsx
<div>
  <button>-</button>
  <span>{count[0]()}</span>
</div>
```
- `Children` is the children slot (not an attribute). [FR-010]
- `Text("-")` literal → JSX text node `-`. [FR-011]
- `Text(count.Value)` non-literal → `{count[0]()}` expression child. [FR-011]
- Child order preserved. [FR-010]

## N4 — Component composition (`new T { … }` in renderable position)

**In**: `new Counter { Count = 3 }` used inside a `Children` list
**Out**: `<Counter count={3} />`
- Component-record `new` in renderable position → `<Name … />`. [FR-012]
- `init` assignments → component attributes, camelCased; literal vs `{…}` per N2. [FR-013]
- `new Counter()` (no initializer) → `<Counter />`. [FR-012]

## N5 — Render-entry lambda (module entry point)

**In**: `Render(() => new CounterGroup(), container);`
**Out**: `render(() => <CounterGroup />, container);` [FR-014, FR-019]

## Invariants

- The implicit `JsxComponent → JsxElement` conversion in the C# source is invisible in output; lowering is decided by the constructed type's `RendersAsJsxElement`. [FR-012]
- Attribute name resolution order: `[Name("…")]` override, else camelCase of the C# property name. No DOM attribute table in the compiler core. [FR-008]
