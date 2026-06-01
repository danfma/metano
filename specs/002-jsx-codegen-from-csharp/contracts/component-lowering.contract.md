# Contract: Component Record → TSX Function Component

**Covers**: FR-001, FR-002, FR-003, FR-004, FR-005, FR-006 (User Story 1)

A contract is an input C# shape paired with the required output TSX shape. Golden tests in `tests/Metano.Tests/Expected/*.tsx` pin these exactly.

## C1 — Empty component (no props)

**In**:
```csharp
[Transpile]
public sealed record Hello : JsxComponent {
    public override JsxElement Render() => new Html.Div { Children = [Text("hi")] };
}
```
**Out** (`hello.tsx`):
```tsx
export type HelloProps = {};

export function Hello(props: HelloProps) {
  return <div>hi</div>;
}
```
- File extension MUST be `.tsx` (contains JSX). [FR-021]
- `props` parameter present even when `HelloProps` is empty. [FR-002]

## C2 — Component with a prop and its default

**In**:
```csharp
[Transpile]
public sealed record Counter : JsxComponent {
    public int Count { get; init; }
    public override JsxElement Render() => new Html.Span { Children = [Text(Count)] };
}
```
**Out** (`counter.tsx`):
```tsx
export type CounterProps = {
  count?: number;
};

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;
  return <span>{props$count}</span>;
}
```
- `count?` optional (no `required` in C#). [FR-004]
- A referenced prop is hoisted once with its C# type default applied. [FR-004]
- Reference to `Count` in the body rewrites to `props$count`. [FR-006]

## C3 — Membership rules (clarification)

Given:
```csharp
public int A { get; init; }          // → prop  (settable)
public string B { get; set; }        // → prop  (settable)
public int C => A + 1;               // EXCLUDED (computed/get-only)
[Ignore] public int D { get; init; } // EXCLUDED ([Ignore])
```
`Props` MUST contain exactly `a?` and `b?`. Positional record parameters also become props. [FR-003]

## C4 — `[Name]` override

**In**: `[Transpile, Name("AppRoot")] public sealed record Root : JsxComponent { … }`
**Out**: `export type AppRootProps = { … }` and `export function AppRoot(props: AppRootProps) { … }`. [FR-005]

## Invariants

- A JSX component never emits a TS `class`, `equals/hashCode/with`, or constructor.
- The function body is the lowered `Render()` body; the final `return` returns JSX.
- Non-JSX types in the same project are unaffected (regression gate).
