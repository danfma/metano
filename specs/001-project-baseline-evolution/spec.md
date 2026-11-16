# Feature Specification: Project Baseline & Multi-Target Evolution

**Feature Branch**: `001-project-baseline-evolution`

**Created**: 2026-05-27

**Status**: Draft

**Input**: User description: "Import this existing project to understand all its current funcionalities, and let's prepare the next evolution steps. A quick note, this project is a transpiler initially focused on C# to TypeScript, but with others backend (like Dart), and even frontends in mind, for the future."

## Context

Metano is an established source-to-source transpiler. In compiler terms it has a
**frontend** (source-language ingestion — today C#/Roslyn), a canonical
**intermediate representation** (IR), and one or more **backends** (target-language
code generators — TypeScript today, Dart partially). The product already ships
substantial functionality and a formal requirements set under `spec/`
(`FR-001`–`FR-048`, `NFR-001`–`NFR-020`), a feature support matrix, an attribute
catalog, a diagnostic catalog, and 24 ADRs.

This feature does **not** add transpiler capability. It (1) establishes a single,
validated **baseline** of what Metano does today, (2) defines a prioritized
**evolution roadmap** for growing into a multi-backend (and eventually multi-frontend)
transpiler, and (3) reconciles the legacy `spec/` material so the project keeps a
single source of truth. Each concrete evolution increment (e.g. Dart parity, a new
backend) is delivered later through its own specification.

## Clarifications

### Session 2026-05-27

- Q: Where should the canonical baseline + roadmap live, and what happens to the legacy `spec/`? → A: The speckit feature directory becomes canonical; migrate legacy descriptions/specs into the speckit format, and rename `spec/` → `old-spec/` as a comparison reference kept only until the new spec reaches parity, then retire it.
- Q: How should baseline traceability (capability → code + test) be captured? → A: Extend the migrated feature-support matrix with `Code area` and `Test` columns — one artifact carries both status and traceability.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authoritative capability baseline (Priority: P1)

A maintainer, adopter, or AI agent needs to answer "what does Metano support **today**?"
from one authoritative source that provably matches the code, instead of inferring from
scattered docs, samples, and ADRs.

**Why this priority**: A roadmap is only trustworthy if the starting point is accurate.
The baseline is the prerequisite for every later decision and is independently valuable
on its own (onboarding, support, AI context).

**Independent Test**: Pick any capability the baseline marks "Implemented" and confirm a
corresponding code path and test exist; pick any "Partial"/"Planned" item and confirm the
baseline's stated gaps match reality. The baseline passes if there is no drift in either
direction.

**Acceptance Scenarios**:

1. **Given** the baseline inventory, **When** a reader looks up any current capability,
   **Then** its status (Implemented / Partial / Planned) and constraints are stated and
   match the codebase and tests.
2. **Given** the frontend/IR/backend framing, **When** a reader inspects the baseline,
   **Then** C#/Roslyn is identified as the current frontend, the IR as the canonical
   contract, and TypeScript (implemented) and Dart (partial) as the current backends.
3. **Given** a capability with constraints, **When** the reader checks it, **Then** the
   specific unsupported sub-cases are enumerated rather than left implicit.

---

### User Story 2 - Prioritized multi-target evolution roadmap (Priority: P2)

A maintainer needs a prioritized, traceable list of evolution thrusts so future work can
be picked up as discrete specifications in the right order, with the multi-target SPI
hardening recognized as the lead thrust.

**Why this priority**: Once the baseline exists, the team needs an agreed direction.
The roadmap turns intent ("more backends, eventually more frontends") into ordered,
testable commitments without committing implementation in this feature.

**Independent Test**: Each roadmap thrust can be read as a standalone future feature with
a clear outcome, a priority rank, and a link back to the baseline gap or vision it
addresses; the SPI-hardening thrust is ranked first.

**Acceptance Scenarios**:

1. **Given** the roadmap, **When** a maintainer reads the top-ranked thrust, **Then** it is
   "harden the multi-target SPI" — stabilize the IR contract and backend port so a new
   backend is additive (no edits to the core or existing backends).
2. **Given** the roadmap, **When** a maintainer reads subsequent thrusts, **Then** Dart
   backend parity and a frontend-extensibility posture are present, each ranked and scoped.
3. **Given** any roadmap thrust, **When** it is selected for delivery, **Then** it carries
   enough definition (outcome + priority + baseline traceability) to seed its own
   `/speckit-specify` without re-deriving context.

---

### User Story 3 - Single source of truth (legacy spec reconciliation) (Priority: P3)

A maintainer must not be left with two competing normative bodies (the legacy `spec/`
directory and this baseline). The legacy material is reorganized, migrated, or retired so
exactly one authoritative source remains.

**Why this priority**: Duplicate specs cause drift — the exact problem the product itself
fights. Valuable, but only after the baseline and roadmap exist to migrate toward.

**Independent Test**: After reconciliation there is no requirement that exists in two
places with conflicting wording; every retained legacy requirement is either preserved
verbatim under the canonical structure or explicitly superseded with a recorded rationale.

**Acceptance Scenarios**:

1. **Given** the legacy `spec/` documents, **When** reconciliation completes, **Then** each
   normative item is migrated, kept, or retired with a stated reason — none silently dropped.
2. **Given** the reconciled state, **When** a reader looks for the current requirements,
   **Then** a single entry point identifies where the authoritative requirements live.
3. **Given** stable identifiers (`FR-###`, `NFR-###`, `MS####`), **When** reconciliation
   completes, **Then** those identifiers remain resolvable so existing references in
   issues, tests, and commits do not break.

---

### Edge Cases

- A capability is present in code but absent from the baseline → baseline is incomplete and
  MUST be corrected (drift in the "missing" direction).
- A capability is documented as Implemented but no longer works / was removed → baseline
  MUST downgrade it and note the change.
- A legacy requirement conflicts with an ADR or current behavior → reconciliation MUST flag
  the conflict and resolve it explicitly, not silently pick one.
- A roadmap thrust depends on another (e.g. Dart parity benefits from SPI hardening) →
  dependency MUST be stated so ordering is not accidental.
- A future frontend (non-C# source language) is hypothesized → the baseline/roadmap MUST
  keep the IR description frontend-agnostic rather than assuming C# is the only input.

## Requirements *(mandatory)*

### Functional Requirements

Baseline (capability inventory):

- **BR-001**: The baseline MUST present Metano's architecture using compiler terminology:
  frontend (source ingestion), IR (canonical semantic representation), and backends
  (target code generators).
- **BR-002**: The baseline MUST identify C#/Roslyn as the sole current frontend and MUST
  NOT assert that C# is the only possible frontend forever.
- **BR-003**: The baseline MUST enumerate current backends with status — TypeScript
  (Implemented, reference backend) and Dart/Flutter (Partial) — including Dart's known gaps.
- **BR-004**: The baseline MUST inventory current capabilities (type selection, type/enum/
  async/exception/pattern/operator/overload lowering, collections, LINQ, modules, output &
  packaging, serialization, type guards, diagnostics, assets) with a status of Implemented,
  Partial, or Planned, and MUST enumerate constraints for Partial items.
- **BR-005**: Every capability marked Implemented in the baseline MUST be traceable to code
  and at least one test; every Partial/Planned item MUST state what is missing. Traceability
  MUST be captured by extending the migrated feature-support matrix with `Code area` and
  `Test` columns, so a single artifact carries both status and traceability.
- **BR-006**: The baseline MUST preserve the existing stable identifier spaces (`FR-###`,
  `NFR-###`, attribute catalog, `MS####` diagnostics) so prior references remain valid.

Roadmap (evolution direction):

- **RR-001**: The roadmap MUST list evolution thrusts as discrete, prioritized items, each
  with an intended outcome and traceability to a baseline gap or product-vision statement.
- **RR-002**: The roadmap MUST rank "harden the multi-target SPI" as the highest-priority
  thrust: stabilize the IR contract and the backend port so adding a backend requires
  changes only in the new backend, with zero edits to the core or existing backends.
- **RR-003**: The roadmap MUST include "Dart backend to parity" as a thrust, scoped to the
  baseline's enumerated Dart gaps (classic extension methods, `[ModuleEntryPoint]` body,
  JSON serializer context, and any others surfaced during baseline capture).
- **RR-004**: The roadmap MUST include a "frontend-extensibility posture" thrust that keeps
  the IR frontend-agnostic and records additional source languages as a deferred future
  direction (no new frontend implemented or committed by this feature).
- **RR-005**: Each roadmap thrust MUST carry enough definition to seed its own future
  specification without re-deriving baseline context, and MUST state dependencies on other
  thrusts where they exist.
- **RR-006**: The roadmap MUST NOT include implementation of any thrust; this feature
  delivers documentation and prioritization only.

Reconciliation (single source of truth):

- **CR-001**: The reconciliation MUST classify every legacy `spec/` normative item as
  migrated, retained, or retired, with a recorded reason for retirement or supersession.
- **CR-002**: The speckit feature directory MUST become the canonical home for the baseline
  and roadmap; legacy `spec/` content MUST be migrated into the speckit specification format
  rather than left as a parallel normative source.
- **CR-003**: The legacy directory MUST be renamed `spec/` → `old-spec/` and retained only
  as a comparison reference; it MUST NOT be treated as authoritative once renamed.
- **CR-004**: `old-spec/` MUST only be retired (deleted) after the migrated speckit baseline
  reaches parity with it — i.e., every retained normative item from `old-spec/` is present
  (migrated or explicitly superseded) in the canonical speckit source. Until that parity
  gate passes, `old-spec/` stays in the tree.
- **CR-005**: After reconciliation a single authoritative entry point MUST identify where
  the current requirements live; no normative item may exist in two places with conflicting
  wording.
- **CR-006**: Reconciliation MUST keep stable identifiers resolvable (no broken `FR-###`/
  `NFR-###`/`MS####` references) even when documents are moved or merged into the speckit
  format.
- **CR-007**: Because the canonical home moves, any document that points to `spec/` as the
  single source of truth (notably the project constitution and `CLAUDE.md`) MUST be updated
  to point at the new canonical location; the conflict MUST be surfaced, not left stale.
- **CR-008**: Reconciliation decisions affecting architecture MUST stay consistent with the
  existing ADRs (notably the target-agnostic core and shared-IR ADRs); any divergence MUST
  be surfaced rather than applied silently.

Product requirements added after the migration (normative; each one is traced by a row of
[baseline/feature-support-matrix.md](./baseline/feature-support-matrix.md) and continues the
`FR-###` space after `FR-048`):

- **FR-049** (constructor access modifiers, #432): The TypeScript backend MUST emit a C#
  constructor's accessibility on the class constructor, and the IR MUST carry the declared
  accessibility on every constructor (`IrConstructorDeclaration`) so other backends can map it
  later (the Dart mapping is out of scope).
  1. *Mapping.* `private` and `private protected` emit `private constructor`; `protected` emits
     `protected constructor`; `public`, `internal` and `protected internal` emit no modifier.
     Record positional constructors and C# 12 primary constructors follow the same mapping: C#
     has no syntax to declare their accessibility, so they are public and emit no modifier,
     including an abstract type's, which Roslyn synthesizes `protected` only to restate that the
     type cannot be instantiated. An implicit constructor emits no modifier. A value type
     (`struct`, `record struct`) always emits a public constructor: C# gives it a public implicit
     parameterless constructor, so `new S()` is legal anywhere whatever it declares. In a
     dispatcher of a reference type, an overload signature no other package can reach — declared `internal`,
     or `private` under a wider emitted modifier — additionally carries the `/** @internal */`
     JSDoc (#445), so `stripInternal` drops only those signatures; the implementation signature
     is untagged. A `protected` or `protected internal` constructor is untagged: a derived class
     in another package may call it. When every signature would be tagged, none is, and a single `internal` constructor is
     untagged too: `stripInternal` would otherwise leave the `.d.ts` class an implicit public
     `new X()` that C# never declared, so such a constructor is widened to the `.d.ts`.
  2. *Mixed accessibility.* Every declared instance constructor, whatever its accessibility,
     takes part in the emitted constructor: two or more go through the overload dispatcher (#435).
     Spec 013 extends that dispatcher's contract (FR-050) — constructor factories (`fromXxx`),
     selector symbols (`protected static readonly by<TypeName>Xxx: unique symbol`) and direct
     call sites. The constructor keeps the accessibility this requirement defines; each factory
     carries its own overload's accessibility, and rule 3's widening applies to a factory that
     code outside the class body calls
     ([specs/013-overload-factories/spec.md](../013-overload-factories/spec.md), DECIDED-17).
     C# makes every other constructor of a type with a record positional or C# 12 primary
     constructor chain to it, so each branch inlines it: its promoted members are declared as
     fields (a member that only reads the parameter is not emitted as a getter), and the branch
     assigns them and the captured parameters' fields and runs the field and property
     initializers that read a parameter, moved into it. A TypeScript class has one constructor
     accessibility: every overload
     signature and the implementation must share it (TS2385). When a type's constructors mix
     accessibility, the backend MUST emit the most permissive mapped modifier (public — which
     `internal` and `protected internal` map to — then protected, then private) and report `MS0060` (`MixedConstructorAccessibility`, warning)
     naming the narrower constructors.
  3. *Construction outside the class body.* When generated or lowered code constructs the type
     from outside its TypeScript class body, a non-public constructor would fail to type-check
     (TS2673 / TS2674, or TS2322 where the class is passed as a constructor value). The backend
     MUST then keep the constructor public and report `MS0061`
     (`ConstructorAccessibilityWidened`, warning) naming the path. The paths are: (a) the
     type's `TypeSpec` in a generated JSON `SerializerContext` (`type: T`, typed
     `abstract new (...) => T`, and `factory: (p) => new T(...)`); (b) a `new T(...)` written in
     a type nested in `T` (C# lets a nested type call its container's private or protected
     constructor; the backend emits nested types outside the container's class body, in the
     companion namespace or module-local as `Owner$Inner`). `[PlainObject]` (an interface plus
     object literals), `[Branded]` / `[InlineWrapper]` (a brand alias whose `create` casts),
     the `[ObjectArgs]` `create` factory (a static member inside the class body) and record
     `with` (copied through `copyRecordWith`) construct nothing outside a class body.
  4. *Derived types.* TypeScript forbids extending a class whose constructor is private
     (TS2675). When an emitted class directly extends `T` and `T`'s constructor maps to
     `private` — a type nested in `T` (including the module-local `Owner$Inner` of #412), or,
     for a `private protected` constructor, any derived type of the assembly — the backend MUST
     emit `protected constructor` for `T`, and only for `T`. When code outside the class body
     also constructs `T`, rule 3 applies and the constructor stays public.

  A referenced package decides its own types' constructors: when a consumer's
  `SerializerContext` constructs a referenced class whose constructor rules 1 and 2 make
  non-public, the backend MUST report `MS0062` (`JsonContractConstructorNotPublic`, warning) at
  the context, since its `TypeSpec` does not type-check. The check uses the predicate the
  emitter uses. Known false positive: a type its package kept public under rule 3. A project
  reference loads as source, so the predicate sees it exactly as the package's own transpile
  did; only a binary reference (a DLL or a package) is read from metadata, where a record's
  positional constructor counts as public and so does an abstract type's `protected`
  parameterless constructor, the one C# synthesizes. There, metadata adds a known false
  positive, an abstract class's C# 12 primary constructor (`protected` in metadata), and a known
  false negative, an abstract type's declared `protected` parameterless constructor.

- **FR-050** (overload dispatch, ADR-0008): The TypeScript backend MUST lower a C# method,
  constructor or user-defined operator overload group (two or more members of one name) to one
  public runtime dispatcher plus one fast path per overload that holds the body.
  1. *Dispatcher.* The dispatcher carries one overload signature per C# overload and a
     `(...args: unknown[])` implementation whose branches test the argument count and each
     parameter's runtime check (ADR-0009), in `OverloadDispatchOrder` (required-only overloads
     first, most parameters first; then optional ones, fewest first; then `params` ones; a
     concrete overload before a same-arity generic one), and throws when no branch matches. A
     constructor dispatcher holds each overload's body in its branch (chains inlined); a method
     dispatcher delegates to the fast paths.
  2. *Defaults and `params`.* A parameter with a default is optional in the signatures and
     binds the C# default when omitted (#438, #440); a `params` parameter takes the expanded
     trailing arguments.
  3. *Catch-all overloads.* A catch-all overload (no parameter checked) tried before an
     overlapping concrete one reports `MS0063` (#440).
  4. *Extension.* Spec 013 ([specs/013-overload-factories/spec.md](../013-overload-factories/spec.md))
     extends this contract with core-computed, root-anchored fast-path names, constructor
     factories, selector symbols and direct call sites; the dispatcher stays the public fallback
     for hand-written TypeScript.

  This restates the legacy overload-dispatch requirement, numbered `FR-042` in the retired
  `old-spec/04-functional-requirements.md`. That identifier keeps resolving through the
  reconciliation ledger (SC-005), but new references cite FR-050: feature specs reuse `FR-042`
  locally (for example 010 FR-042, the union companion namespace), so the bare number is
  ambiguous. Traced by the "Method overload dispatch" and "Constructor overload dispatch" rows of
  the feature-support matrix.
- **FR-051** (numeric format strings and interpolation clauses, #323, #308): A format clause
  or an alignment clause of an interpolation hole (`{value,alignment:format}`), and
  `ToString(format)` / `ToString(format, provider)` of a BCL type, MUST NOT be dropped
  silently, and what printed .NET's text for every value before this requirement MUST keep
  printing it. Each backend either honors the clause with .NET invariant-culture parity or
  reports it. Legacy FR-014 (string interpolation) and #308 (the formatting policy) are
  the context.
  1. *IR.* The core MUST carry the format string, the constant alignment and the value's
     numeric kind (looking through `Nullable<T>`, not an enum) on the interpolation hole,
     target-agnostic. A hole with a clause carries its value unchanged (a `null` included);
     a hole without one keeps the null-safe concatenation operand. A format C# ignores is
     dropped: composite formatting passes it only to an `IFormattable`, so a static type
     that does not implement it (a `string`, a `bool`, a user class, record or struct)
     ignores it, and `char` ignores it too; `object`, an interface and a type parameter keep
     it, since the run-time type decides. The rule reads the static type: it is exact for a
     sealed class, a struct and a record struct, while for a non-sealed class whose run-time
     value implements `IFormattable` (`Open x = new DerivedFmt(); $"{x:X}"`) .NET calls the
     derived `ToString(format, provider)` and the output prints the default text. An
     alignment still applies.
  2. *TypeScript.* A hole with a clause MUST lower to one `metano-runtime` call inside the
     template literal, dispatched statically by the value's C# type: `formatInteger(value,
     bits, format, alignment?)` for `sbyte`, `byte`, `short`, `ushort`, `int` and `uint`;
     `formatInt64` for `long` / `ulong` (spec 012 FR-037); `formatDouble(value, format,
     alignment?)` for `double`; `alignValue(value, alignment)` for an alignment without a
     format, which pads the text the hole prints without the clause. `ToString(format)` on
     the same types MUST call the same formatter (the provider is ignored and not
     evaluated). The formatters MUST produce .NET's invariant-culture output for the
     standard formats `G[n]`, `R`, `F[n]`, `N[n]`, `E[n]`, `P[n]`, `C[n]`, and `D[n]` /
     `X[n]` for the integers, with any precision and both letter cases: a `double` rounds
     its exact binary value half to even (.NET Core 3.0+), an integer rounds half away from
     zero, a negative value rounding to zero keeps its sign, `NaN` and the infinities
     print as .NET prints them, a `null` value prints as `""`, an alignment pads with
     spaces counting UTF-16 code units (left when positive, right when negative), and a
     format .NET rejects throws the same `FormatException`. Parity is verified against
     .NET by the reference fixture `tests/Int64Reference/fixtures/number-formatting.json`
     under Bun and Node.
  3. *Formats without a formatter.* A hole is `value.ToString(format)`. Where no formatter
     takes the format, the hole MUST lower, in order: to the clause-free hole when the
     target declares that its clause-free text is .NET's text for that format for every
     value (TypeScript: a `Guid`'s `D`, a numeric enum's `D`, a `[StringEnum]`'s `G` / `F`
     with no renamed member, a `DateOnly`'s `O` and its `yyyy-MM-dd`, the same ISO text, a
     `BigInteger`'s `G` / `R` / `D` without a precision); through the
     declarative mapping of the type's `ToString(string)` when one lowers it, for a
     non-nullable value (so a hole and the call cannot disagree; `Guid` maps `N`, `B`,
     `P`); otherwise it is unsupported (rule 4). `ToString(format)` and
     `ToString(format, provider)`, the instance overloads whose first parameter is the
     format (a static `ToString`, such as `Convert.ToString(string)`, is not a format call),
     of any BCL type (a number, an enum, a date or time,
     `Guid`, `IFormattable.ToString` on a type parameter) follow the same rule: an existing
     declarative mapping lowers it, a default-text format becomes the clause-free hole, and
     anything else is unsupported, and a `value?.ToString(format)` takes the same steps on
     the bound receiver (`null` when the value is `null`). An empty or `null` constant
     format is the type's own default format, as in .NET (`G` for an enum or a
     `BigInteger`, `D` for a `Guid`, the short date for a `DateOnly`). A user type's own
     `ToString(string)` is not affected; neither is a type compiled from source or a
     transpiled library, whatever its assembly name: only an assembly that is not a source
     assembly, is named `System` / `System.*` (or the core library) and does not carry
     `[TranspileAssembly]` / `[EmitPackage]` is a BCL assembly. The exclusion rests on
     source-ness or the Metano attributes: a project reference Roslyn retargets is not a
     source assembly, so one named `System.*` without the attributes is treated as BCL.
  4. *Unsupported clauses.* A clause the target does not format as .NET does MUST report
     `MS0001` (warning) at the hole, or at the format argument of `ToString(format)`, and
     lower to the throwing placeholder (in a static field initializer the module then
     throws when imported): in TypeScript, a constant custom numeric format (`0.00`,
     `#,##0`), a constant `B` on an integer, and any other format on a type rule 3 does not
     cover (`decimal`, `float`, `Half`, the other date and time formats, an enum's other
     formats, a user `IFormattable` type, `object`, a type parameter), constant or not. A
     `ToString(format)` whose format is not a constant, on an integer or a `double`, MUST
     call the formatter with the format expression (receiver and format each evaluated
     once, in C# order), which decides at run time: a `null` format is `G`, as
     `ToString((string?)null)` is on .NET; a custom format or `B` throws
     `NotSupportedException` naming the format (`The format '0.00' is not supported for
     double in TypeScript.`); a format .NET rejects throws .NET's `FormatException`. The
     64-bit integers (`long`, `ulong`, `nint`, `nuint`) keep `MS0066` for their unsupported
     constant formats (spec 012 FR-037; unifying it with `MS0001` is #662). Which formats
     a target honors is the target's own fact, declared through the extraction context's
     format-string port; a
     target that declares none (Dart today, and any future target) fails closed: every
     hole with a clause (other than a dropped format) and every BCL `ToString(format)`,
     constant or not, reports, naming the target.
  5. *Default conversion unchanged.* A hole without a clause, and `ToString()` without a
     format, MUST emit exactly what they emitted before; the default conversion of `bool`,
     `double` and `float` stays the rest of #308.
     An alignment over a clause-free text that still differs from .NET's (a `bool`, an
     enum, a `decimal`'s trailing zeros; #308, #662) pads that text.
  6. *Out of scope.* This requirement does not cover `string.Format` composite formatting
     (`string.Format("{0:F2}", d)`, which keeps its unmapped call and its `MS0001`) or
     `FormattableString`, both tracked in #662, nor custom numeric format patterns
     (reported, rule 4), including the ones that equal the default text for every value of
     a narrow type (`{b:#,##0}` on `sbyte` / `byte`) or a `DateOnly` (`{d:yyyy'-'MM'-'dd}`,
     quoted separators), and `BigInteger`'s `D0` / `D1` (equal to `D` for every value),
     which stay reported. Known silent gap:
     `Guid`'s `X` and an invalid `Guid` format reach the fallback `Guid.ToString` mapping,
     which prints the hyphenated text; restricting the fallback needs the mapping port to
     see literal filters (#657).

- **FR-052** (caller argument expressions): A call to a method or constructor with a
  `[CallerArgumentExpression("p")]` parameter that the call leaves out MUST pass the source text
  of the call's argument for `p`, positional or named, in every target, as the C# compiler
  does; this covers the argument guards (`ArgumentNullException.ThrowIfNull(x)` passes `"x"`).
  An argument written for the caller-expression parameter itself keeps the call's own value. A
  defaulted parameter the call omits before the caller-expression parameter MUST receive the
  default the call site sees, the one declared by the method the call binds to. In TypeScript a
  callee that applies that same default itself (a transpiled, non-virtual method or constructor
  that is not `[Emit]`, nor bound elsewhere by `[Import]` or `[External]` on itself or its type) is passed `undefined`;
  any other callee (one of those, a delegate, an interface, abstract, virtual or override
  member, whose runtime implementation may declare a different default) is passed the value.
  Dart always passes the value. Not filled yet: a transpiled extension method called with
  extension syntax (`x.Ext()`, lowered to a module function), a `base(...)` / `this(...)`
  constructor initializer, an explicit delegate `.Invoke(...)` and an `[Emit]` callee. Traced by the "Caller
  argument expressions" row of the feature-support matrix.

- **FR-056** (LINQ aggregates by element type, #618, #657): `Sum`, `Average`, `Min`, `Max`,
  `MinBy` and `MaxBy` in TypeScript MUST give what .NET gives for the element type. `Min` / `Max`
  of a non-nullable sequence and `MinBy` / `MaxBy` of a value-typed element throw
  `InvalidOperationException` when empty; of a `Nullable<T>` or reference result they skip nulls and
  give `null` when no value is left. `double` `NaN` wins `Min` and loses `Max`. `decimal` sums and
  averages use decimal.js arithmetic (`sumDecimal`, `averageDecimal`), and `decimal`, `string`,
  date and other ordered values are ordered by `compareKeys` (strings in ordinal order, which
  differs from .NET's culture-sensitive order for mixed case). Traced by the "LINQ pipe lowering" row
  of the feature-support matrix.

- **FR-057** (LINQ set operators, #657): `Union`, `Intersect` and `Except` MUST lower to the
  pipe operators `union`, `intersect` and `except` of `metano-runtime/system/linq`, comparing
  elements by value as `HashSet` does and yielding distinct elements in .NET's order. The overloads
  that take a comparer report MS0001 unless the comparer is `null`.

- **FR-058** (LINQ static call form, #619, #657): `Enumerable.X(xs, ...)` /
  `Queryable.X(xs, ...)` for a method that has a pipe operator MUST lower to the same pipe as
  `xs.X(...)`, including when nested in a chain. A static call that names an argument reports
  MS0001. The legacy fluent `Enumerable.from(...)` mappings no longer exist.

### Key Entities

- **Frontend**: The source-language ingestion stage (C#/Roslyn today) that produces the IR.
- **Intermediate Representation (IR)**: The canonical, language-neutral semantic model that
  decouples frontend from backends — the stable contract the evolution centers on.
- **Backend (Target)**: A target-language code generator consuming the IR (TypeScript;
  Dart, partial). Each backend implements the backend port against the IR.
- **Capability Baseline**: The validated inventory of current functionality with statuses,
  constraints, and code/test traceability.
- **Roadmap Thrust**: A prioritized, future-facing evolution item with outcome, priority,
  dependencies, and baseline/vision traceability.
- **Legacy Spec Item**: A normative statement in the existing `spec/` directory subject to
  migration, retention, or retirement during reconciliation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of capabilities marked "Implemented" in the baseline are traceable to a
  code path and at least one test; reviewers find zero drift in either direction during
  validation.
- **SC-002**: A reader unfamiliar with the project can answer "what does Metano support
  today, and what is the next priority?" using only the baseline and roadmap, in under 15
  minutes, without reading source code.
- **SC-003**: The roadmap ranks the multi-target SPI hardening as the #1 thrust, and a
  maintainer can describe, from the roadmap alone, the additive-only target: adding a new
  backend touches only the new backend's own artifacts.
- **SC-004**: After reconciliation there are zero requirements duplicated across the legacy
  `spec/` and the canonical source with conflicting wording, and 100% of legacy normative
  items have a recorded disposition (migrated / retained / retired).
- **SC-005**: Every stable identifier referenced before this feature (`FR-###`, `NFR-###`,
  `MS####`) still resolves to a definition after reconciliation (no dangling references).
- **SC-006**: Every roadmap thrust is self-contained enough that a reviewer can start its
  own specification from the thrust entry without requesting additional context.

## Assumptions

- This feature is **documentation and planning only** — baseline capture, roadmap
  definition, and legacy reconciliation. No transpiler code is changed here.
- "Frontend" and "backend" follow compiler terminology: frontend = source-language
  ingestion (C#/Roslyn), backend = target-language generation (TypeScript, Dart). The
  earlier informal use of those words is corrected accordingly.
- Additional source-language frontends and framework-specific frontend code generation are
  out of scope for this feature; they are recorded only as deferred future directions, and
  the architecture is kept from foreclosing them.
- The existing `spec/` content is broadly accurate and serves as primary raw material; it is
  migrated into the speckit specification format (the new canonical home), renamed to
  `old-spec/` as a comparison reference, and retired only after the canonical baseline
  reaches parity with it — preserving normative intent and stable identifiers throughout.
- Concrete delivery of any roadmap thrust (Dart parity, a new backend, SPI changes) happens
  in separate, later specifications and is governed by the project constitution at delivery
  time.
- The 24 existing ADRs remain authoritative for architectural rationale; the baseline and
  roadmap reference them rather than restating or overriding them.
