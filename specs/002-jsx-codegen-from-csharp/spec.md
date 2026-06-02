# Feature Specification: JSX/TSX Code Generation from C# Components

**Feature Branch**: `002-jsx-codegen-from-csharp`

**Created**: 2026-06-01

**Status**: Draft

**Input**: User description: "First attempt at generating JSX directly from C#. Prototyped usage in `samples/SampleSolidUi/` plus new SolidJS binding projects under `bindings/Metano.TypeScript.SolidJs/`. C# renderable record components (a `JsxComponent` whose `Render()` returns a `JsxElement`) should be rewritten as JSX function components; record `init` properties become a `Props` type; native HTML builders and component records become JSX elements; the marker types in `src/Metano/Annotations/TypeScript/` let the compiler recognize per-library renderable types via structural typing."

## Overview

Metano today transpiles C# to TypeScript `.ts` files containing classes, interfaces, functions, and plain-object DTOs. It cannot yet express **UI components** — there is no way to author a component in C# and have Metano emit idiomatic JSX/TSX consumable by a JSX-based runtime such as SolidJS.

This feature introduces a **first vertical slice** of UI component transpilation: a C# *renderable record component* (a record deriving from a builder base whose `Render()` method returns a marked renderable element type) is lowered to an idiomatic **JSX/TSX function component**. Its `init` properties become a generated `Props` type; element-builder object initializers in the body become JSX elements; and the calling framework's reactivity and helper primitives (SolidJS signals, `For`, `render`) are mapped through declarative bindings.

The recognition of "what is a renderable element" is **library-agnostic by design**: marker attributes (`[JsxComponentBuilder]`, `[JsxNativeElement]`, `[External]`, `[Import]`) and marker types (`JsxElement`, `IJsxComponentBuilder<,>`) let the compiler identify renderable types per binding library, leaning on TypeScript's structural typing so output is consumable by any JSX runtime whose element type is structurally compatible. SolidJS is the **proving target** for v1, and recognition is additionally validated against at least one *imported* library type (e.g. a component imported from `solid-router`) to confirm the marker set is sufficient beyond hand-authored native elements.

## Clarifications

### Session 2026-06-01

- Q: Target-library scope for this first attempt (v1)? → A: Solid + generic — SolidJS is the proving target, AND the library-agnostic recognition mechanism is validated against at least one imported library type (e.g. `solid-router`) to prove the marker set generalizes beyond native HTML.
- Q: Does automatic reactivity (C# field/property mutation lowered to signal read/write) belong in scope, or only the explicit signal API? → A: Explicit signal API only. Lowering is mechanical: `Solid.CreateSignal` returns a `[JsTuple]` `Signal<T>` destructured into a getter+setter pair, the getter (`Func<T>`) is invoked as `count()`, the setter (a `[JsCallable]` interface) is invoked as `setCount(v)`, plus `CreateEffect`/`For`/`render`. Automatic field-mutation reactivity (the dataflow "hard 80%") is explicitly **out of scope** and deferred. *(Updated: this clarification originally described an `ISignal<T>.Value`/`.Set` facade lowering to tuple-index forms (`count[0]()`/`count[1]()`); it now adopts the feature-003 `[JsTuple]`/`[JsCallable]`/tuple-deconstruction primitives — see Dependencies.)*
- Q: How is the JSX attribute name derived (e.g. `ClassName` → `class`)? → A: camelCase of the C# property name by default (`ClassName` → `className`, `OnClick` → `onClick`), with `[Name("...")]` on the binding property as the override for HTML-literal forms. The SolidJS binding carries `[Name("class")]` on `Html.Element.ClassName` so its output is `class` (aligned with the Solid idiom); no built-in DOM attribute table lives in the compiler core.
- Q: Which component members become props? → A: Settable properties (`{ get; init; }` / `{ get; set; }`) and record positional parameters become props; get-only / computed / expression-bodied / readonly members are treated as derived/internal and excluded from the `Props` type. `[Ignore]` excludes any member.
- Q: How is the signal setter emitted? → A: Idiomatic direct form via the `[JsCallable]` setter — `setCount.Invoke(value)` → `setCount(value)` and `setCount.Invoke(updater)` → `setCount(updater)`; the value is not wrapped in `() => value`. The setter's two `Invoke` overloads (value and updater) both lower to a direct call. *(Updated from the prior `ISignal.Set(...)` facade form.)*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Renderable record component becomes a TSX function component (Priority: P1)

A developer authors a C# `sealed record` deriving from the renderable builder base (`JsxComponent`) and overrides `Render()` to return a `JsxElement`. Metano transpiles it into a single idiomatic TSX function component: a named `export function`, a sibling `export type <Name>Props`, and the `Render()` body lowered into the function body and `return`ed JSX.

This is the spine of the feature — without it, none of the element/attribute/reactivity lowering has a host to live in. It delivers the end-to-end "C# component in, TSX component out" experience.

**Why this priority**: It is the minimal viable slice. A single component with no children and no props still proves the core pipeline: builder-base detection, props extraction, body lowering, JSX return, and `.tsx` emission.

**Independent Test**: Transpile a component record whose `Render()` returns one native element (no children, no props) and assert the output is a `.tsx` file containing `export function <Name>(props: <Name>Props)` and a `<Name>Props` type, with the element returned as JSX.

**Acceptance Scenarios**:

1. **Given** a `sealed record Counter : JsxComponent` with no declared properties, **When** transpiled, **Then** the output is `Counter.tsx` containing `export function Counter(props: CounterProps)` and an `export type CounterProps = {}` (empty props), and `Render()`'s returned element is emitted as JSX inside a `return`.
2. **Given** a component record with `public int Count { get; init; }`, **When** transpiled, **Then** `CounterProps` declares `count?: number` (optional, because C# has no `required` keyword) and the body that references `Count` reads it through a props-derived local with the C# type default applied.
3. **Given** the component file contains at least one JSX element, **When** emitted, **Then** the file extension is `.tsx` (not `.ts`).
4. **Given** a `[Name("X")]` override on a component record, **When** transpiled, **Then** the function and the `Props` type both honor the overridden name (`X` and `XProps`).

---

### User Story 2 - Native HTML element builders lower to intrinsic JSX elements (Priority: P2)

Inside `Render()`, the developer instantiates native element builders via object initializers (e.g. `new Html.Div { ClassName = "counter", Children = [ ... ] }`). Each builder marked as a native element lowers to an intrinsic JSX element using its declared tag, with object-initializer assignments becoming JSX attributes and the `Children` collection becoming nested JSX child nodes.

**Why this priority**: Native elements are the leaves and containers of every component tree. Without them the function body has nothing meaningful to return.

**Independent Test**: Transpile a component returning `new Html.Div { ClassName = "x", Children = [ ... ] }` and assert the output is `<div class="x">...</div>` with children nested and text nodes inlined.

**Acceptance Scenarios**:

1. **Given** a builder `new Html.Div { ClassName = "counter" }` whose type carries `[JsxNativeElement("div")]`, **When** transpiled, **Then** it emits `<div class="counter" />` (or with children, an open/close pair), using the declared tag name.
2. **Given** an initializer assignment `ClassName = "x"`, **When** transpiled, **Then** it emits the JSX attribute `class="x"` (HTML attribute-name mapping, not the C# property name).
3. **Given** an `OnClick = handler` assignment, **When** transpiled, **Then** it emits `onClick={handler}` (event-handler attribute with an expression value).
4. **Given** a `Children = [a, b, c]` collection, **When** transpiled, **Then** each element is emitted as a nested JSX child in order.
5. **Given** a `Text("...")` call (or `Text(expression)`), **When** transpiled, **Then** it emits a JSX text node (literal text) or an interpolated expression child (`{expression}`).

---

### User Story 3 - Component records lower to JSX component usage (Priority: P2)

When a component record (a renderable builder, not a native element) is instantiated and used where a renderable element is expected — e.g. `new Counter()` inside a `Children` list, or `() => new CounterGroup()` passed to `render` — it lowers to JSX **component** usage (`<Counter />`), with its `init` property assignments becoming component attributes.

**Why this priority**: Composition (components rendering other components) is what makes the feature useful beyond a single leaf. It reuses the Props contract produced by User Story 1.

**Independent Test**: Transpile a component that returns `new Counter { Count = 3 }` as a child and assert it emits `<Counter count={3} />`.

**Acceptance Scenarios**:

1. **Given** `new Counter()` used in a renderable position, **When** transpiled, **Then** it emits `<Counter />`.
2. **Given** `new Counter { Count = 3 }`, **When** transpiled, **Then** it emits `<Counter count={3} />` mapping initializer assignments to component attributes (camelCased prop names).
3. **Given** the implicit conversion from a builder to the marked element type (`JsxComponent → JsxElement`), **When** a builder appears in a renderable position, **Then** Metano treats that initializer block as JSX rather than a runtime object allocation.
4. **Given** `render(() => new CounterGroup(), container)` at the module entry point, **When** transpiled, **Then** the lambda body emits `<CounterGroup />`.

---

### User Story 4 - SolidJS reactivity and helper primitives map to their runtime equivalents (Priority: P2)

The developer uses the SolidJS binding surface — `Solid.CreateSignal` (returning a `[JsTuple]` `Signal<T>` destructured into a getter `Func<T>` + a `[JsCallable]` setter), `Solid.CreateEffect`, `Solid.For`, and `SolidRenderer.Render` — and Metano maps each to its idiomatic SolidJS runtime form, eliding the C# binding types.

**Why this priority**: A UI component is inert without state. The explicit-signal API is the reactivity model for v1; it must lower cleanly for the proving target.

**Independent Test**: Transpile a component using `var (count, setCount) = Solid.CreateSignal(0); ... count() ... setCount.Invoke(...)` and assert it emits `const [count, setCount] = createSignal(0)` with reads as `count()` and writes as `setCount(...)`, importing `createSignal` from `solid-js`.

**Acceptance Scenarios**:

1. **Given** `var (count, setCount) = Solid.CreateSignal(value)`, **When** transpiled, **Then** it emits `const [count, setCount] = createSignal(value)` imported from `solid-js`, and the C# `Signal<T>`/`ISignalSetter<T>` binding types are fully erased (no wrapper object is allocated in output).
2. **Given** a read `count()`, **When** transpiled, **Then** it emits `count()` (a plain `Func<T>` invocation).
3. **Given** a write `setCount.Invoke(v)` and `setCount.Invoke(fn)`, **When** transpiled, **Then** each emits `setCount(v)` / `setCount(fn)` (the `[JsCallable]` direct-call form), preserving value-vs-updater semantics.
4. **Given** `Solid.CreateEffect(() => ...)`, **When** transpiled, **Then** it emits `createEffect(() => ...)` imported from `solid-js`.
5. **Given** `Solid.For(items, (item, index) => element)`, **When** transpiled, **Then** it emits Solid's `<For each={items}>{(item, index) => ...}</For>` component usage.
6. **Given** `SolidRenderer.Render(fn, container)`, **When** transpiled, **Then** it emits `render(fn, container)` imported from `solid-js/web`.

---

### User Story 5 - Library-agnostic renderable-type recognition (Priority: P3)

The marker attributes and types let Metano recognize "what counts as a renderable element" for any binding library — hand-authored native elements, the builder base, and types **imported** from external packages (e.g. a component from `solid-router`) — without hard-coding SolidJS. The recognition rule keys off the marked element type and the marker attributes, leaning on structural typing.

**Why this priority**: This is the generality guarantee. It must be proven against at least one imported library type so the design is not silently overfit to native HTML.

**Independent Test**: Declare an imported renderable type (typed as / convertible to the marked element type, carrying `[Import]`) and use it in a renderable position; assert it is recognized and emitted as JSX component usage with the correct import, not as a runtime object.

**Acceptance Scenarios**:

1. **Given** a type declared with `[JsxComponentBuilder]` (the builder base), **When** a derived record is transpiled, **Then** Metano recognizes it as a component and applies User Story 1 lowering.
2. **Given** a native element type carrying `[JsxNativeElement("tag")]`, **When** used in a renderable position, **Then** it lowers to the intrinsic `<tag>` element.
3. **Given** a renderable type carrying `[Import("Name", from: "package")]` (e.g. a `solid-router` component), **When** used in a renderable position, **Then** Metano emits it as JSX component usage and adds the corresponding import.
4. **Given** the marker set is insufficient to recognize a renderable type or distinguish a component from a native element, **When** transpiled, **Then** Metano reports a clear diagnostic rather than producing incorrect output (verification gate for marker sufficiency).

---

### Edge Cases

- **No JSX in the file**: a transpilable type with no JSX element still emits `.ts` (not `.tsx`); the `.tsx` switch is triggered only by the presence of at least one JSX element.
- **Prop with no explicit default**: an `init` property without an initializer uses its C# type default (e.g. `int` → `0`, reference type → `null`); the generated prop is optional (`name?: T`) because C# cannot mark it `required`.
- **Prop referenced multiple times in the body**: the props-derived local is materialized once and reused (no repeated `props.x ?? default`).
- **Empty children / self-closing**: an element with no children emits a self-closing JSX tag.
- **Mixed children**: a `Children` list mixing native elements, component records, `Text(...)`, and helper calls (`Solid.For(...)`) preserves order and lowers each child by its own rule.
- **Attribute value that is an expression vs literal**: string-literal attributes emit `attr="literal"`; non-literal expressions emit `attr={expr}`.
- **Nested component composition**: a component returning other components (e.g. `CounterGroup` rendering `Counter` via `Solid.For`) lowers each composed component correctly.
- **Erased binding types**: the `[JsTuple]` `Signal<T>` and `[JsCallable]` `ISignalSetter<T>` binding types are erased — no `.ts` declaration is emitted for either, and `Solid.CreateSignal` collapses to the `createSignal(...)` runtime call, leaving no trace of the C# binding types in output.
- **Reserved/colliding attribute names**: HTML attribute mapping (`ClassName` → `class`) must not collide with JSX/TS reserved words; the mapping is explicit per native element binding.

## Requirements *(mandatory)*

### Functional Requirements

#### Component recognition & shape

- **FR-001**: The system MUST recognize a C# type as a *renderable component* when it derives from a builder base marked `[JsxComponentBuilder]` (implementing the `IJsxComponentBuilder<TSelf, TElement>` marker contract) and overrides the render method returning the marked element type.
- **FR-002**: The system MUST transpile a recognized component record into a single named `export function` component whose parameter is a generated props object.
- **FR-003**: The system MUST generate a sibling `export type <Name>Props` object type from the component's settable properties (`{ get; init; }` / `{ get; set; }`) and record positional parameters, using camelCased member names. Get-only / computed / expression-bodied / readonly members are derived/internal and MUST be excluded from `Props`; `[Ignore]` excludes any member.
- **FR-004**: Generated props MUST be optional (`name?: T`) because C# has no `required` keyword; the component body MUST apply each property's C# type default (or explicit default) when the prop is read.
- **FR-005**: The system MUST honor `[Name("...")]` on a component, applying the override to both the function name and the `Props` type name (`<Override>Props`).
- **FR-006**: The system MUST lower the render method body into the generated function body, returning the produced JSX element.

#### Native element lowering

- **FR-007**: The system MUST lower an object-initializer of a type marked `[JsxNativeElement("tag")]` into an intrinsic JSX element using the declared tag name.
- **FR-008**: The system MUST map native-element initializer assignments to JSX attributes by camelCasing the C# property name (`ClassName` → `className`, `OnClick` → `onClick`), and MUST honor `[Name("...")]` on the binding property as an override for HTML-literal attribute names. (The SolidJS binding declares `[Name("class")]` on `Html.Element.ClassName`, so its emitted attribute is `class`.) No DOM attribute-name table is hard-coded in the compiler core.
- **FR-009**: The system MUST emit string-literal attribute values as quoted attributes (`attr="literal"`) and non-literal expression values as braced attributes (`attr={expr}`), including event handlers (`OnClick` → `onClick={handler}`).
- **FR-010**: The system MUST lower a `Children` collection into ordered nested JSX child nodes, and MUST emit a self-closing tag when there are no children.
- **FR-011**: The system MUST lower the renderable text helper (`Text(...)`) into a JSX text node for literal content and a braced expression child for non-literal content.

#### Component composition

- **FR-012**: The system MUST lower the instantiation of a component record used in a renderable position into JSX component usage (`<Name ... />`), driven by the implicit conversion from the builder to the marked element type.
- **FR-013**: The system MUST map a composed component's `init` property assignments to JSX component attributes (camelCased), emitting literal vs braced values per FR-009.
- **FR-014**: The system MUST lower a lambda/expression that produces a component at the module entry point (e.g. the argument to `render`) into JSX usage of that component.

#### Reactivity & helper mapping (explicit signal API only)

- **FR-015**: The system MUST map the signal-creation binding (`Solid.CreateSignal(value)`, returning a `[JsTuple, Import("Signal", from: "solid-js")]` `Signal<T>` record) to `createSignal(value)` imported from `solid-js`, and MUST erase the `Signal<T>` / `ISignalSetter<T>` binding types so no wrapper object or declaration is emitted. A consuming `var (count, setCount) = Solid.CreateSignal(value)` MUST lower to `const [count, setCount] = createSignal(value)` via tuple-deconstruction. *(Realized on the feature-003 `[JsTuple]` + tuple-deconstruction primitives.)*
- **FR-016**: The system MUST map a signal read (the getter `Func<T>` invocation `count()`) to `count()`, and a signal write (the `[JsCallable]` setter) to the idiomatic direct call form: `setCount.Invoke(value)` → `setCount(value)` and `setCount.Invoke(updater)` → `setCount(updater)` (the value MUST NOT be wrapped in `() => value`). The setter's two `Invoke` overloads (value and updater) both lower to a direct receiver invocation. *(Realized on the feature-003 `[JsCallable]` primitive; no `[Emit]` template, no `count[0]()`/`count[1]()` index form.)*
- **FR-017**: The system MUST map the effect binding (`Solid.CreateEffect(action)`) to `createEffect(...)` imported from `solid-js`.
- **FR-018**: The system MUST map the list-rendering helper (`Solid.For(items, (item, index) => element)`) to Solid's `<For each={items}>{(item, index) => ...}</For>` component usage.
- **FR-019**: The system MUST map the render-entry binding (`SolidRenderer.Render(fn, container)`) to `render(fn, container)` imported from `solid-js/web`.
- **FR-020**: The system MUST scope reactivity lowering to the explicit signal API only; automatic conversion of C# field/property mutation into reactive read/write is explicitly out of scope for this feature.

#### File emission & library-agnostic recognition

- **FR-021**: The system MUST emit a file with the `.tsx` extension when (and only when) it contains at least one JSX element; files without JSX continue to emit `.ts`.
- **FR-022**: The system MUST recognize renderable types **imported** from external packages (carrying `[Import("Name", from: "package")]` and typed as / convertible to the marked element type) and emit them as JSX usage with the correct import, without hard-coding any specific library.
- **FR-023**: The system MUST add the imports required by mapped bindings and recognized imported components (e.g. `createSignal`/`createEffect` from `solid-js`, `render` from `solid-js/web`, `For` from `solid-js`, router components from `solid-router`) to the generated module, merging names from the same package per existing import-merging rules.
- **FR-024**: The system MUST emit a clear diagnostic (rather than silently producing incorrect output) when the marker set is insufficient to recognize a renderable type or to distinguish a component from a native element. This requirement is the verification gate for marker-set sufficiency.

### Key Entities *(include if feature involves data)*

- **Renderable component**: a C# record deriving from the `[JsxComponentBuilder]` base whose render method returns the marked element type; the unit transpiled into a TSX function component.
- **Props type**: the generated `<Name>Props` object type derived from a component's settable properties (`init`/`set`) and record positional parameters; all members optional with type defaults applied in the body. Derived/computed members are excluded.
- **Native element**: a builder type marked `[JsxNativeElement("tag")]`; lowers to an intrinsic JSX element.
- **Marked element type (`JsxElement`)**: the abstract renderable type the conversion targets; its presence in a position signals "emit as JSX."
- **Builder contract (`IJsxComponentBuilder<TSelf, TElement>`)**: the marker interface tying a builder to the element type it renders, including the implicit `TSelf → TElement` conversion.
- **Signal binding (`Signal<T>` / `ISignalSetter<T>`)**: the explicit reactive primitive — a `[JsTuple]` positional record (getter `Func<T>` + `[JsCallable]` setter) that erases into the target framework's signal tuple form. Built on the feature-003 `[JsTuple]`/`[JsCallable]` primitives.
- **Reactivity / helper bindings**: the binding surface (`Solid.CreateSignal`, `CreateEffect`, `For`, `SolidRenderer.Render`) mapped to runtime equivalents via declarative import/emit mappings.
- **Imported renderable type**: a renderable element/component sourced from an external package via `[Import]` (e.g. `solid-router`), recognized library-agnostically.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The prototype `SampleSolidUi` project (`Counter` + `CounterGroup` + entry point) transpiles end-to-end into `.tsx` output with zero manual edits required to make it a valid JSX module.
- **SC-002**: A developer can author a stateful counter component in C# and obtain idiomatic SolidJS TSX output whose function/props shape matches the documented target (named function, `<Name>Props` type, signal-based body) with no wrapper objects from the C# abstraction surviving in the output.
- **SC-003**: 100% of the reactivity/helper bindings in the prototype (`CreateSignal` + tuple-deconstruction, getter `count()`, `[JsCallable]` setter `setCount(...)`, `CreateEffect`, `For`, `render`) lower to their documented SolidJS runtime forms — with no `count[0]()`/`count[1]()` index forms and no surviving `Signal`/`ISignalSetter` artifact — verified by golden-output tests.
- **SC-004**: Library-agnostic recognition is proven against at least two distinct sources — native HTML elements **and** at least one type imported from an external package (e.g. `solid-router`) — with both emitted as correct JSX and imports added.
- **SC-005**: The generated SolidJS output compiles and runs (renders the expected DOM) in a consuming Vite + SolidJS sample, mirroring the existing `sample-counter-*` consumer pattern.
- **SC-006**: When the marker set cannot recognize a renderable type, the transpiler surfaces an actionable diagnostic instead of emitting silently-wrong code, in 100% of the identified unsupported-shape cases.

## Assumptions

- **Explicit-signal reactivity only**: reactivity is expressed through the explicit signal API (`Solid.CreateSignal` → destructured getter/setter, `CreateEffect`); automatic field-mutation reactivity is deferred to a future feature (confirmed in Clarifications; consistent with the deferral recorded in prior reactive-lowering investigation).
- **Depends on feature 003**: the signal binding is realized via the `[JsTuple]`, `[JsCallable]`, and tuple-deconstruction primitives delivered by feature `003-js-interop-primitives`. The original tuple-index facade (`ISignal.Value`/`.Set` → `count[0]()`/`count[1]()`) is superseded by that composition.
- **SolidJS is the proving target**: v1 validates the pipeline against SolidJS, while the recognition mechanism is designed library-agnostic and additionally validated against one imported library type. React/Vue/Svelte targets are out of scope for this feature.
- **The prototype bindings are the contract**: `bindings/Metano.TypeScript.SolidJs/` and `bindings/Metano.TypeScript.DOM/` define the intended binding surface; this feature implements the transpiler behavior that makes those prototypes lower correctly. The binding API may be refined if a marker proves insufficient (FR-024).
- **Attribute names are camelCased by default, overridable per binding**: the default attribute name is the camelCased C# property name (`ClassName` → `className`, `OnClick` → `onClick`); HTML-literal names (`class`, `for`) are opt-in via `[Name("...")]` on the binding property. The compiler core hard-codes no DOM attribute table, keeping recognition library-agnostic.
- **TypeScript structural typing carries cross-library compatibility**: output is consumable by any JSX runtime whose element type is structurally compatible with the marked element type; Metano does not emit per-library type adapters.
- **Existing import-merging and `[Import]`/`[Emit]` machinery is reused** for adding/merging the imports that mapped bindings require.
- **Reuses existing sample/test conventions**: validation follows the established `sample-counter-*` consumer pattern and TUnit golden-output tests with `Expected/` fixtures.

## Out of Scope

- Automatic conversion of C# field/property mutation into reactive signal read/write (dataflow reactive lowering).
- Targets other than JSX/SolidJS (React, Vue, Svelte, Flutter/Dart JSX-equivalents).
- A bespoke UI markup syntax (Razor/XAML-style); components are authored as ordinary C# records and object initializers.
- Lifecycle/control-flow primitives beyond those exercised by the prototype (`For`); `Show`, `Switch`, `Index`, stores, context, and resources are future work.
- Server-side rendering, hydration, and routing behavior beyond recognizing imported router component **types**.
