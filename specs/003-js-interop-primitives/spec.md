# Feature Specification: JS-Interop Foundational Primitives (`[JsTuple]`, `[JsCallable]`, Tuple Deconstruction)

**Feature Branch**: `003-js-interop-primitives`

**Created**: 2026-06-01

**Status**: Draft

**Input**: User description: "Two new TypeScript-target annotations plus tuple deconstruction that let binding authors model JS array-tuples and callable values without hand-written `[Emit]` templates: `[JsTuple]` (positional record → JS array tuple, the array-shape parallel to `[PlainObject]`), `[JsCallable]` (erased interface whose `Invoke(...)` lowers to direct receiver invocation, supporting overloaded `Invoke`), and `var (a,b) = expr` → `const [a,b] = expr`. Motivating consumer: rewrite the SolidJS signal binding so `Solid.CreateSignal<T>` returns a `[JsTuple, Import(\"Signal\", from:\"solid-js\")] record Signal<T>(Func<T> Getter, [JsCallable] ISignalSetter<T> Setter)`, yielding idiomatic `const [count, setCount] = createSignal(0); count(); setCount(v)` with zero `[Emit]`. Native C# ValueTuple → TS tuple is out of scope (deferred)."

## Overview

Metano lets binding authors describe how C# surfaces map to a JS/TS runtime. Today two common JS shapes have **no clean declarative representation** and force hand-written `[Emit]` templates with positional placeholders:

1. **Array-tuples** — a value that is a JS array used positionally, e.g. SolidJS's `createSignal` returning `[getter, setter]`. The current binding models this through an `ISignal` facade whose members carry `[Emit("$0[0]()")]` / `[Emit("$0[1]($1)")]`, producing cryptic transpiled code (`count[0]()`, `count[1](v)`).
2. **Callable values with multiple call signatures** — a JS function that accepts more than one argument shape (e.g. a setter taking either a value or an updater function). A C# delegate cannot express overloaded call signatures, and `[Emit("$0($1)")]` must be repeated per method.

This feature introduces two **declarative, reusable** TypeScript-target primitives plus the tuple-deconstruction lowering that ties them together:

- **`[JsTuple]`** — marks a positional record as a JS array-tuple. It is the array-shape sibling of `[PlainObject]` (which is the object-shape DTO marker).
- **`[JsCallable]`** — marks an erased interface modeling a JS callable; calls to its `Invoke(...)` lower to direct invocation of the receiver, and overloaded `Invoke` is allowed (which delegates cannot express).
- **Tuple deconstruction** in variable declarations — `var (a, b) = expr` → `const [a, b] = expr`.

Together these let binding authors write idiomatic C# that lowers to idiomatic JS with **no `[Emit]` templates**. The proving consumer is the SolidJS signal binding. Both attributes are TypeScript-specific and live in `Metano.Annotations.TypeScript` (no-op for other targets). Native C# `ValueTuple` mapping is explicitly deferred.

## Clarifications

### Session 2026-06-01

These decisions were settled in design discussion before specification and are normative:

- Q: Element shape of the signal — interface-with-`[Emit]` vs delegates? → A: Getter is a pure single-signature function → `Func<T>` (invoked as `count()`, zero `[Emit]`). Setter needs value+updater overloads → a `[JsCallable]` interface with overloaded `Invoke` (delegates cannot overload).
- Q: Tuple wrapper — native `ValueTuple` vs a named record? → A: A `[JsTuple]` record (named, importable, parallels `[PlainObject]`). Native `ValueTuple` is deferred as a future general/cross-target primitive.
- Q: Callable marker name? → A: `[JsCallable]` (describes the type as callable; pairs with the `Invoke` method).
- Q: Namespace? → A: Both `[JsTuple]` and `[JsCallable]` live in `Metano.Annotations.TypeScript` (TS-specific knobs; other targets treat them as no-ops), consistent with `[External]`/`[Jsx*]`.
- Q: Diagnostic code allocation? → A: New diagnostics start at **MS0027** (`MS0026` is reserved by the in-flight JSX feature on branch `002-jsx-codegen-from-csharp`, which is not yet merged).

## User Scenarios & Testing *(mandatory)*

The "user" here is a **binding author** describing a JS library's surface in C#, and (transitively) any developer whose code consumes that binding and reads the transpiled output.

### User Story 1 - `[JsCallable]` interface lowers `Invoke` to direct invocation (Priority: P1)

A binding author models a JS callable value (with one or more call signatures) as an erased interface marked `[JsCallable]` declaring overloaded `Invoke` methods. Calls to `recv.Invoke(args)` lower to `recv(args)`.

**Why this priority**: It is the primitive that removes the most hand-written `[Emit]` and is the only way to express an overloaded JS call signature (delegates can't). It is independently useful for any library callable.

**Independent Test**: Declare `[JsCallable] interface ICb<T> { void Invoke(T v); void Invoke(Func<T,T> f); }`, call `cb.Invoke(5)` and `cb.Invoke(x => x+1)`; assert output is `cb(5)` and `cb(x => x + 1)`, and no `.Invoke` member survives.

**Acceptance Scenarios**:

1. **Given** a `[JsCallable]` interface with `void Invoke(T value)`, **When** `cb.Invoke(v)` is transpiled, **Then** it emits `cb(v)`.
2. **Given** overloaded `Invoke(T)` and `Invoke(Func<T,T>)`, **When** each is called, **Then** both lower to direct invocation preserving the argument (`cb(v)`, `cb(f)`), with no `[Emit]` template authored.
3. **Given** `Invoke` of arbitrary arity (0..n args), **When** transpiled, **Then** all arguments pass through positionally (`recv(a, b, c)`).
4. **Given** a `[JsCallable]` interface, **When** the project is transpiled, **Then** no declaration file/type is emitted for it (it is erased), and it composes with `[Import]`/`[External]`.

---

### User Story 2 - `[JsTuple]` record lowers to a JS array-tuple (Priority: P1)

A binding author marks a positional record `[JsTuple]` so it represents a JS array used positionally. Synthesized record members are suppressed, positional member access lowers to array index, and the record may alias an imported library tuple type via `[Import]`.

**Why this priority**: It is the array-shape primitive (sibling of `[PlainObject]`), required to model `createSignal`'s `[get, set]` and any positional JS tuple.

**Independent Test**: Declare `[JsTuple] record Pair<A,B>(A First, B Second);`, produce one (via an `[Import]`/`[Emit]` factory), access `.First`/`.Second`; assert `.First` → `[0]`, `.Second` → `[1]`, and no `equals`/`hashCode`/`with`/class is emitted for `Pair`.

**Acceptance Scenarios**:

1. **Given** a `[JsTuple]` positional record, **When** transpiled, **Then** no class wrapper and no synthesized `equals`/`hashCode`/`with` are emitted (shape-only, like `[PlainObject]`).
2. **Given** positional member access `pair.First` (declaration index 0), **When** transpiled, **Then** it emits `pair[0]`; `pair.Second` → `pair[1]`.
3. **Given** `[JsTuple, Import("Signal", from: "solid-js")]`, **When** the type appears as an annotation (e.g. a field/return type), **Then** it resolves to the imported library tuple type (`Signal<T>` from `solid-js`) rather than a generated type.
4. **Given** a `[JsTuple]` type used as the producing function's return (the function is `[Import]`/`[Emit]`), **When** transpiled, **Then** the record itself is never `new`'d in output (the JS factory produces the array).

---

### User Story 3 - Tuple deconstruction in variable declarations (Priority: P2)

A developer (or binding consumer) deconstructs a tuple-typed value in a `var` declaration, mirroring JS array destructuring.

**Why this priority**: It is what makes the array-tuple ergonomic and idiomatic on both sides; without it, consumers fall back to positional index access.

**Independent Test**: Transpile `var (a, b) = makePair();` and assert `const [a, b] = makePair();`; transpile `var (_, b) = makePair();` and assert the first slot is a discard.

**Acceptance Scenarios**:

1. **Given** `var (a, b) = expr;` where `expr` is a `[JsTuple]`-typed value, **When** transpiled, **Then** it emits `const [a, b] = expr;`.
2. **Given** a discard `var (_, b) = expr;`, **When** transpiled, **Then** the unused position is emitted as a destructuring hole/placeholder and only `b` is bound.
3. **Given** the deconstructed locals are then used, **When** transpiled, **Then** references resolve to the destructured names (`a`, `b`), not index access.

---

### User Story 4 - Idiomatic SolidJS signal binding via composition (Priority: P2)

The three primitives compose so the SolidJS signal binding is rewritten with **zero `[Emit]` templates**, and the cryptic `count[0]()` / `count[1]()` output is replaced by idiomatic destructured Solid usage.

**Why this priority**: It is the motivating end-to-end win and the regression anchor — it proves the primitives compose and that the previously-shipped (branch 002) signal output improves.

**Independent Test**: Transpile a component/store using `var (count, setCount) = Solid.CreateSignal(0); ... count() ... setCount(count() + 1); setCount(c => c + 1);` and assert the SolidJS output: `const [count, setCount] = createSignal(0); ... count() ... setCount(count() + 1); setCount(c => c + 1);` with no `[Emit]`/index forms and no surviving facade type.

**Acceptance Scenarios**:

1. **Given** `Solid.CreateSignal<T>` returning `[JsTuple, Import("Signal", from:"solid-js")] record Signal<T>(Func<T> Getter, [JsCallable] ISignalSetter<T> Setter)`, **When** `var (count, setCount) = Solid.CreateSignal(0)` is transpiled, **Then** it emits `const [count, setCount] = createSignal(0)`.
2. **Given** a read `count()`, **When** transpiled, **Then** it emits `count()` (a plain `Func<T>` invocation, no `[Emit]`).
3. **Given** writes `setCount.Invoke(v)` and `setCount.Invoke(c => c + 1)`, **When** transpiled, **Then** they emit `setCount(v)` and `setCount(c => c + 1)` (via `[JsCallable]`, no `[Emit]`).
4. **Given** the rewritten binding, **When** the existing SolidJS consumer samples are transpiled and built, **Then** they build and run with no `count[0]()`/`count[1]()` forms and no `ISignal`/wrapper artifacts in output.

---

### Edge Cases

- **`[JsTuple]` on a non-positional record** (no positional parameters): reported with a diagnostic (`MS0027`) — there is no positional shape to map to array slots.
- **`[JsCallable]` interface declaring non-`Invoke` members**: reported with a diagnostic (`MS0028`) — a callable models only the call operation; other members have no array/call lowering.
- **`[JsTuple]` value used without deconstruction**: falls back to positional member/index access (`sig.Getter` → `sig[0]`), which still works but is less idiomatic.
- **Deconstruction arity mismatch** vs the tuple's element count: rejected by the C# compiler itself (no Metano diagnostic needed).
- **Nested deconstruction** (`var ((a, b), c) = …`): out of scope for this iteration (flat deconstruction only); a nested form is not guaranteed.
- **`[JsCallable]` value passed as an argument / stored** (not invoked): the underlying JS value is a function, so it passes through; the C# interface type is erased.
- **`[JsTuple]` composing with `[Import]` whose arity differs** from the record's positional count: the binding author's responsibility; the imported type is trusted as the annotation.

## Requirements *(mandatory)*

### Functional Requirements

#### `[JsCallable]`

- **FR-001**: The system MUST recognize `[JsCallable]` on an interface and lower any call to its `Invoke(...)` method into a direct invocation of the receiver (`recv.Invoke(args)` → `recv(args)`).
- **FR-002**: The system MUST support **overloaded** `Invoke` declarations (multiple call signatures) on a `[JsCallable]` interface, lowering each call to direct invocation while preserving the passed arguments.
- **FR-003**: The system MUST pass `Invoke` arguments through positionally for any arity (0..n), with no placeholder template authored by the binding.
- **FR-004**: The system MUST treat a `[JsCallable]` interface as erased — no declaration is emitted for it — and MUST allow it to compose with `[Import]`/`[External]`.
- **FR-005**: The system MUST emit a diagnostic (`MS0028`) when `[JsCallable]` is applied to a non-interface, or when a `[JsCallable]` interface declares members other than `Invoke`.

#### `[JsTuple]`

- **FR-006**: The system MUST recognize `[JsTuple]` on a positional record and represent it as a JS array-tuple (positional), not an object literal or class.
- **FR-007**: The system MUST suppress synthesized record members (`equals`/`hashCode`/`with`) and MUST NOT emit a class/interface declaration for a `[JsTuple]` record (shape-only, mirroring `[PlainObject]`).
- **FR-008**: The system MUST lower positional member access on a `[JsTuple]` value to array index access by declaration order (`value.<Member_i>` → `value[i]`).
- **FR-009**: The system MUST allow `[JsTuple]` to compose with `[Import]`, resolving the type's annotation to the imported library tuple type (e.g. `Signal<T>` from `solid-js`) instead of a generated type.
- **FR-010**: The system MUST emit a diagnostic (`MS0027`) when `[JsTuple]` is applied to a type with no positional shape (e.g. a non-positional record/class).

#### Tuple deconstruction

- **FR-011**: The system MUST lower a deconstructing variable declaration `var (a, b, …) = expr` into JS array destructuring `const [a, b, …] = expr`.
- **FR-012**: The system MUST handle discard elements (`var (_, b) = expr`) by emitting a destructuring hole / omitted binding for the discarded position.
- **FR-013**: The system MUST resolve later references to deconstructed names to the destructured bindings (not to index access).

#### Scope & composition

- **FR-014**: The three primitives MUST compose so a binding of the form `[JsTuple, Import(…)] record Signal<T>(Func<T> Getter, [JsCallable] ISignalSetter<T> Setter)` consumed via `var (count, setCount) = factory()` produces idiomatic destructured output (`const [count, setCount] = …`, `count()`, `setCount(v)`) with no `[Emit]` templates.
- **FR-015**: Both `[JsTuple]` and `[JsCallable]` MUST reside in `Metano.Annotations.TypeScript` and MUST be treated as no-ops by non-TypeScript targets.
- **FR-016**: Native C# `ValueTuple` (`(T, U)`) → TS tuple mapping is explicitly **out of scope** for this feature; it is deferred as a future general/cross-target primitive.

### Key Entities *(include if data involved)*

- **`[JsTuple]` record**: a positional record that represents a JS array-tuple; the array-shape sibling of `[PlainObject]`. Members are positional slots; no class is emitted.
- **`[JsCallable]` interface**: an erased interface modeling a JS callable value; its `Invoke` overloads lower to direct receiver invocation.
- **Tuple deconstruction**: the `var (a, b) = expr` → `const [a, b] = expr` lowering that binds positional slots to names.
- **Signal binding (motivating consumer)**: `Signal<T>(Func<T> Getter, ISignalSetter<T> Setter)` — a `[JsTuple]` record (importing Solid's `Signal`) whose setter is a `[JsCallable]` interface; the proving ground for composition.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A `[JsCallable]` interface's `Invoke` call (including overloaded value/updater forms) lowers to direct receiver invocation with zero authored `[Emit]` templates, verified by golden-output tests.
- **SC-002**: A `[JsTuple]` record + deconstruction produces `const [a, b] = expr` and positional `[i]` access, with no class/`equals`/`hashCode`/`with` emitted, verified by golden-output tests.
- **SC-003**: The SolidJS signal binding rewritten on these primitives produces idiomatic destructured output (`const [count, setCount] = createSignal(0); count(); setCount(v); setCount(c => c+1)`) — 100% free of `count[0]()`/`count[1]()` index forms and of any `ISignal`/wrapper artifact — verified by golden tests AND by the existing SolidJS consumer sample(s) building and running unchanged.
- **SC-004**: Misuse of either marker (`[JsTuple]` on a non-positional type; `[JsCallable]` on a non-interface or with non-`Invoke` members) surfaces an actionable diagnostic (`MS0027`/`MS0028`) instead of silently-wrong output, in 100% of the identified misuse cases.
- **SC-005**: No regression to existing transpilation — non-`[JsTuple]` records, `[PlainObject]`, and all current golden tests are unchanged.

## Assumptions

- **Positional records only for `[JsTuple]`**: the array mapping uses positional declaration order; non-positional shapes are a diagnostic, not a guess.
- **`Invoke` is the sole callable member name**: `[JsCallable]` keys off the conventional .NET `Invoke` name (mirrors delegate semantics); other member names are not the call operation.
- **Flat deconstruction only**: nested deconstruction is out of scope this iteration.
- **Erasure composes with existing `[External]`/`[Import]`/`[Emit]` machinery**: `[JsTuple]`/`[JsCallable]` reuse the established erasure and import channels rather than introducing new emission paths where avoidable.
- **Diagnostic numbering coordinates with in-flight branches**: `MS0026` is reserved by branch `002`; this feature uses `MS0027`+.
- **The motivating consumer is the regression anchor**: the SolidJS signal binding rewrite (and its sample consumer) validates composition end to end.

## Out of Scope

- Native C# `ValueTuple` `(T, U)` → TS tuple (deferred general/cross-target primitive; would also map to Dart 3 records).
- Nested tuple deconstruction (`var ((a, b), c) = …`).
- Deconstruction in non-declaration positions (assignment-deconstruction `(a, b) = expr`, foreach deconstruction) — may be added later.
- Tuple literals as construction sites (`return (a, b)`) and `.ItemN` access on native ValueTuples — tied to the deferred ValueTuple work.
- Any change to the JSX feature (branch `002`) beyond defining the primitives it will later consume; the 002 reactivity refactor that adopts `Signal<T>` is a separate change that depends on this spec.

## Dependencies

- **Consumed by**: the reactivity refactor of spec `002-jsx-codegen-from-csharp` (replacing the `ISignal.Value/.Set` facade with the `Signal<T>` `[JsTuple]` + `[JsCallable]` binding). That refactor is tracked separately and depends on this feature landing first.
- **Reuses**: existing `[Import]`/`[External]`/`[Emit]` erasure + import machinery, the `[PlainObject]` shape-only emission pattern (as the design sibling), and the diagnostics catalog.
