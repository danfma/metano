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

  **Default-text correction (#694, TypeScript).** Rule 5 preserves the output of the
  original formatting work, except for the explicitly tracked default-text gaps corrected
  by #694: numeric enum names (including the .NET comma-space list for flags), boxed and
  nullable Boolean/Double and generic Boolean/Double text, an empty string from a null
  nullable's parameterless `ToString()`, TimeSpan constant (`c`) text, ValueTuple text
  enclosed in parentheses, constructed decimal scale in concatenation, and null string
  compound targets treated as empty while evaluating their receiver/index once. The
  explicit enum `D` format remains numeric. Decimal literal/parse scale is presentation
  metadata read only by default-text helpers; native decimal.js construction, coercion
  and value hashing remain normalized and unchanged. Enums without emitted descriptors
  keep their previous text lowering without synthetic reflection diagnostics.
  These corrections use declarative mappings
  and existing neutral IR; Dart output remains unchanged. Generic Single type erasure,
  arithmetic decimal scale propagation (#662), invariant casing of sharp-s and trimming
  U+FEFF remain separate follow-ups.

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
  Dart always passes the value. The text is filled for every call shape: an extension method
  called with extension syntax (`x.Ext()`, lowered to a module function), a `base(...)` /
  `this(...)` constructor initializer (a `this(...)` chain passes it as an argument named after
  the parameter), an explicit delegate `.Invoke(...)` or `?.Invoke(...)` and an `[Emit]`
  callee, whose template never prints an unfilled `$N`. A parameter of a transpiled
  struct type whose default is `default` (`Point p = default`) receives `new Point()` in the
  callee and in a named call that skips it, when the struct declares no constructor (so
  the target's parameterless one runs); a struct with a declared or primary constructor keeps the
  opaque default (`undefined`). Traced by the "Caller argument expressions" row of the
  feature-support matrix.

- **FR-053** (string members with .NET arguments, #661): A `System.String` member whose
  JavaScript namesake reads its arguments differently or clamps where .NET throws — `Substring`,
  `Remove`, `Insert`, `PadLeft` / `PadRight`, `ToCharArray(start, length)`, `IndexOf` /
  `LastIndexOf` with a start index, count or `StringComparison`, `StartsWith` / `EndsWith` /
  `Contains` with a `StringComparison`, `Trim` / `TrimStart` / `TrimEnd` with characters, `Split`
  with characters, a count or `StringSplitOptions`, and `string.Join(separator, values, start,
  count)` — MUST lower in TypeScript to a `metano-runtime` helper that returns .NET's value for
  valid arguments, evaluates each argument once and throws .NET's `ArgumentOutOfRangeException`
  / `ArgumentNullException` (type, parameter name and message) for invalid ones. Comparisons
  are ordinal; the culture-sensitive `StringComparison` members behave as their ordinal
  counterparts. The helpers MUST agree with rows recorded from real .NET under Bun and Node
  (`tests/BclReference`). Not covered: `AsSpan` / `Slice` on strings and spans, culture-aware
  comparison. Traced by the "String members with .NET arguments" row of the feature-support
  matrix.

- **FR-054** (construction and parsing members, #673, #668): `new TimeSpan(...)` from
  components, `TimeSpan.Zero` and the single-argument `TimeSpan.FromX`, `new DateTimeOffset(...)`
  from components, a `DateTime` or ticks plus an offset, `Guid.Parse`, `decimal.TryParse`, and
  `Parse` / `TryParse` of `int`, `uint`, `short`, `ushort`, `byte`, `sbyte` and `Parse` of
  `double`, `float` and `bool` MUST lower in TypeScript to `metano-runtime` functions with .NET's
  value and exceptions (`ArgumentOutOfRangeException`, `ArgumentException`, `OverflowException`,
  `FormatException`, `ArgumentNullException`): a `TimeSpan` built from components or units is the
  duration .NET builds (never `new Temporal.Duration(0, 5, 0)`), `Guid.Parse` returns the lowercase
  `D` text, and an integer `Parse` rejects what `Number.parseInt` would half-read (`"12abc"`).
  A constant `NumberStyles` the integer parsers do not implement reports `MS0066`. The runtime
  functions MUST agree with rows recorded from real .NET (`tests/BclReference`). Not covered:
  the .NET 9 multi-unit `TimeSpan.FromX(a, b)` forms, `DateTimeOffset(DateTime)` without an
  offset, the `X` GUID format, the `ReadOnlySpan<char>` forms of `Parse`.

- **FR-055** (decimal precision and rounding, #465 part a): The class the TypeScript target
  generates for `decimal` MUST be decimal.js configured for `System.Decimal` — 29 significant
  digits, half-to-even rounding — exported by `metano-runtime/system/decimal`, so no generated
  code relies on decimal.js's global defaults. Known differences remain documented in the
  "`decimal` specifics" section of `docs/bcl-mappings.md` (quotient digits, scale floor, overflow,
  trailing zeros); the exact-scale, overflow and rounding-mode parity of #465 is not claimed.

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

- **FR-059** (target-typed `default`, #627, #372, #612, #409): A `default` literal and
  `default(T)` MUST lower to the default value of the type the compiler converts them to, at
  every position (a local initializer, a conditional arm, a `??` operand, an argument, a
  collection element): `false`, `0`, `0n`, `"\u0000"`, `new Decimal("0")`, `null` for a
  reference or nullable type, the zero-valued member of an enum (a `0` asserted to the enum
  when it declares none), and `new S()` for a transpiled struct that declares no constructor —
  the value FR-052 gives a parameter declared `= default` and the implicit initializer gives an
  unassigned field of that struct. One core policy spells the default for every position: these
  literals, parameter defaults, the miss value of the `OrDefault` LINQ terminals and
  `List<T>.Find`, and the `GetValueOrDefault` fallbacks; the implicit initializer of a field
  reads the same policy (`'\0'`, `Guid.Empty` and a `BigInteger` zero included). A `default`
  literal whose struct construction leaves a field without its default (a type-parameter,
  tuple or `DateTime` field) MUST report `MS0001`. A type no expression spells (a struct
  that declares a constructor, a tuple, a `[StringEnum]` without a zero member) MUST report
  `MS0001` and keep the target's placeholder. The implicit initializer of an enum field MUST be
  the same value, never the lowest member; a `[StringEnum]` field without a zero member gets no
  initializer.

- **FR-060** (record field equality, #674): The generated `equals` of a record MUST compare a
  field as .NET's `EqualityComparer<T>.Default` does. A field of a type that compares by value
  with `===` (integers, `string`, `bool`, enums, branded values) uses `===`; a `double` /
  `float`, `decimal`, the date and time types, `Guid`, tuples and transpiled record and struct
  types use the runtime's `valueEquals`; a field whose static type does not say (a type
  parameter, `object`, an interface, a class) uses the runtime's `defaultEquals`: its own
  `equals` when it has one, `NaN` equal to itself, else reference equality. A plain
  (non-record) struct emitted as a class carries a generated `equals` / `hashCode` over its
  fields (its `ValueType.Equals`) unless it declares its own (an `override Equals(object)`, or an
  `Equals(S)` implementing `IEquatable<S>` or a transpiled interface member), so a struct behind an `object` or
  interface field compares by value. The synthesized `equals` / `hashCode` / `with` keep the
  names the runtime calls: a user method the target spells the same way (`Equals(T other,
  double tol)`, `HashCode(int seed)`, a record's `With(int a)`, an `IEqualityComparer<T>`
  implementation) MUST be joined by the synthesized member as one more overload of its group,
  dispatched under the shared name, with spec 013's naming and anchoring (013 FR-038–FR-045,
  DECIDED-33); a user declaration that replaces a kind supplies its body instead of removing
  it; an overload the dispatcher cannot tell from the synthesized member keeps its fast path
  without a dispatcher branch and reports `MS0063` (an `MS0001` error only when it implements an
  interface member or overrides a root of that name), a field or property emitted as `equals`
  MUST report `MS0001` as an error, and a field `HashCode` keeps its name; an array behind a type parameter, `object` or an interface
  compares element by element, so a tuple there compares by value as on .NET (a tuple is an
  array at run time); a C# array there also compares element-wise where .NET compares the
  reference, as 2.x did (a known gap: the run time cannot tell a tuple from an array). An equality guard
  (`ArgumentOutOfRangeException.ThrowIfEqual` / `ThrowIfNotEqual`) admits a sealed generic
  record, and a record with a `double` / `float` field, when every type argument and field is a
  value of the first kind or one the runtime compares as .NET does.

- **FR-061** (null guards by declared type, #649): The null guard of a string concatenation,
  an interpolation hole, a list pattern's subject and an exception's `message` argument MUST
  follow the declared nullability of the operand, not Roslyn's flow state, which is optimistic
  after `x!`, after a call that clears a checked field or a lambda-cleared local, and absent in
  nullable-disabled code. The guard stays unless the operand cannot be `null` by construction
  (a constant, an interpolated string, a concatenation, an object creation, a value type, or a
  symbol declared non-nullable in a nullable-enabled context).

- **FR-062** (import resolution, #655, #675, #504): The import collector MUST resolve an
  identifier to an export of another module only when nothing in scope binds it: a parameter,
  a declared local, a `foreach` variable, a `catch` parameter or a chain's caught value shadows
  an export of the same name, so no spurious import (and no import cycle) follows. Every
  `using` alias a file spells MUST bind an import (`Widget as W1`, `Widget as W2`), also beside
  an `[ImportAlias]` pin of the same type, and the type binds under its own name only where the
  file spells it so; a `[NoContainer]` factory named like an aliased type imports beside it. A
  type used as a value from a synthesized default (an enum's zero member) imports through its
  package origin. A template text MUST declare the receiver's type with the `$Type` placeholder
  (the exported name, never an alias, imported as a value) instead of naming it: the scan of the
  template text is retired, and Metano's own BCL mappings use the placeholder. Two project
  types printed under one name that one file refers to MUST report `MS0070` at the second
  reference (an import by alias is a follow-up). Traced by the "Import resolution" row of the
  feature-support matrix.

- **FR-063** (side-effect modules, #356, #368): The generated `package.json#sideEffects` MUST
  list the modules that run code when merely imported (a `[ModuleEntryPoint]` module, a
  `[StrictUnionGuard]` variant registration), as their `dist` `.js` and source `.ts` paths, and
  be `false` when there is none, so a bundler keeps a side-effect-only import of them. A flat
  `false` the writer wrote earlier is upgraded; a hand-written value keeps its entries. Traced by
  the "Packaging" row of the feature-support matrix.

- **FR-064** (explicit type arguments, #675): A call that writes its type arguments
  (`Guard.ThrowIfEqual<Shape>(circle, square)`) to a transpiled generic method MUST keep them in
  TypeScript, which would otherwise infer the type from the first operand only. A call whose
  callee is a BCL or untranspiled member keeps the callee's own signature.

- **FR-065** (explicit reference downcasts, #669): An explicit reference conversion
  (`(Derived)value`, `(IShape)obj`, `(string)obj`) MUST give the TypeScript result the target
  type, so a member read through it type-checks. A target with a runtime class (a transpiled
  class, record or struct, a BCL exception with a mapped class) is also checked as .NET checks
  it: `castTo<Derived>(value, Derived)` lets `null` through and throws `InvalidCastException`
  (message `Unable to cast object of type 'A' to type 'B'.`, simple type names) for any other
  value. A target with no runtime class to test (an interface, a type parameter, `string`) is
  only asserted (`value as T`). The result keeps the bare operand where TypeScript's own flow
  analysis already types it (the cast sits where an earlier `operand is T` of the same operand
  holds, through `&&`, `?:` or `if`), for an upcast, and for a cast to a type that mentions a
  type parameter inside an `[Inline]` expansion; it only asserts, without importing the class,
  in a type the target derives from (an import cycle that fails at load), where the class import
  would close a cycle of value imports through other modules (the TypeScript target reads its
  generated import graph), and inside an `[Inline]` expansion. Unboxing casts (`(int)obj`) are not reference conversions and keep the bare
  operand. Traced by the "Explicit reference downcasts" row of the feature-support matrix.

- **FR-066** (static constructors, #426; lazy since #681): A type's static constructor MUST run
  when .NET runs it: once, immediately before the first of these on that type: an instance
  created through one of its declared or emitted constructors, a call to a static method, operator
  or conversion operator it declares, a read or write of a static field or property it declares,
  and, for a struct, the first call to one of its instance methods. Its static field and
  auto-property initializers run first, inside it, in textual order; for a partial type, in the
  order Roslyn lists the declarations (the syntax-tree order of the compilation). These do not
  trigger it: reading a `const` or an `[Inline]` field (folded at the use site), a reference to the
  class itself (`typeof`, `instanceof`, passing the class), a nested type, the statics Metano
  synthesizes (overload selectors, runtime type descriptors), and a struct's default value
  (`default(S)`, an array element, a slot default, `new S()` binding to the implicit parameterless
  constructor). A static member reached through a derived type's name triggers only the declaring
  type (`Derived.M()` with `M` declared on `Base` runs `Base`'s), and `new Derived()` runs
  `Derived`'s before `Base`'s, the reverse of the instance constructors. Re-entry, from the body
  itself or through another type's initializer that reads back, sees the type as initialized,
  with the values assigned so far (C# defaults before their initializer). An exception that
  escapes the initializer is wrapped in a `TypeInitializationException` (the original as
  `InnerException`, .NET's message), and every later trigger rethrows that same exception.

  TypeScript lowers a type that declares a static constructor, even an empty one, without a
  `static { }` block:
  - `static readonly #$init = typeInitializer("Name", () => { … })`, a `metano-runtime` helper, is
    declared before the slots. Its body first assigns the slots whose default is not a literal
    token, then the field and auto-property initializers in textual order, then the constructor
    body. The type name is the one .NET 10 reports in `TypeInitializationException.TypeName`
    and its message: the namespace-qualified metadata name of a top-level type, without type
    arguments (`MyApp.Registry`, `` MyApp.Pair`1 ``), and only the metadata name of a nested type
    (`Inner`, not `MyApp.Outer+Inner`).
  - Each static field and auto-property becomes a private slot `#name$` (the trailing `$`, which
    no C# name spells, keeps it apart from `#$init` and from Metano's own private members such as
    an exception's `#baseMessage`). A slot holds a literal-token default at class definition:
    `null` for a reference or nullable type, `0` / `0n` / `false` for a primitive. Every other
    default (a struct default `new S()`, a property read such as an enum's zero member `E.None` or
    `UUID.empty`) is assigned at the start of `#$init`, so a slot never runs a constructor, a guard
    or a read of another module at class definition. A struct default `new S()` is spelled only for
    a struct that declares no constructor, whose emitted constructor builds the default value and
    so runs neither `S`'s guard nor its initializers; the same holds for a struct-typed instance
    field's default. Plan note: a `null` slot of a non-nullable
    reference type is declared `T | null` (strict-mode valid) and its getter narrows it to `T`. A `static get name()` / `static set name(value)`
    pair calls `Name.#$init()` first. A read-only member has no setter. A `static readonly` field
    or get-only static property that the static constructor writes gets a `private` setter, so it
    stays read-only outside the class.
  - Every static method, operator and conversion method, and every static property accessor,
    starts with `Name.#$init();`.
  - Every instance constructor runs the guard before anything else the instance runs: before
    `super(...)` in a derived class. In a class with no base class, ES class fields run before the
    constructor body, so the guard folds into the first class field, in emitted order, that has an
    initializer (promoted, synthesized and auto-property backing fields included; declaration-only
    fields skipped): `a = (C.#$init(), Other.next());`. When no field has an initializer, the
    guard is the first constructor statement. A constructor dispatcher guards before branching. A class with a static constructor and no
    declared instance constructor gets one that carries the guard (in a derived class
    `constructor(...args) { D.#$init(); super(...args); }`).
  - A struct runs the guard, then its instance field and auto-property initializers, on every path
    of the emitted constructor that runs a constructor it declares, whatever the number of
    arguments (`new S(new int[0])` for `S(params int[] values)` runs both), and on no path that
    builds its implicit default value, which runs no constructor: the whole constructor of a struct
    that declares none, and the leading `args.length === 0` branch of a dispatcher (spec 013
    FR-027), after which the prologue follows. A struct with a static constructor declares its
    instance fields and auto-properties without initializers (defaults only) and assigns their
    initializers in that prologue, after the guard, in textual order, so they never run before it.
    Each of its instance methods and instance property and indexer accessors starts with the
    guard (ECMA-335 I.8.9.5), including auto-property accessors and a record struct's synthesized
    equals. Instance auto-properties use backing storage with guarded accessors; instance fields
    remain unguarded reads and writes (#696).
  - A static member whose TypeScript name is also the name of a slot's accessors (a private field
    `count` beside a property `Count`, both `count`; two slotted members; a slotted member and a
    `const`) is an `MS0001` error at that member, asking for a `[Name]` on one of them. Neither
    member is dropped.
  - Accessors, `#$init` and every guard spell `Name.#x$` / `Name.#$init()`, never `this`:
    `Derived.x` reaches `Base`'s accessor with `this === Derived`.

  A type without an explicit static constructor keeps its static field initializers at class
  definition: .NET's `beforefieldinit` leaves their timing to the runtime (.NET 10 runs them at
  the first static field access). Exception (#696): a derived type whose eager static initializer
  constructs itself and thereby triggers a lazy base must defer its static storage initialization
  until first use, so the base can read that derived singleton after the derived class exists.

  `TypeInitializationException` is a `metano-runtime` class extending the runtime
  `SystemException`, bound with `[ExportFromBcl(typeof(TypeInitializationException))]` like its
  siblings, with `TypeName` mapped. It passes the inner exception to `super(message, inner)`, so
  `InnerException` reads `cause`; a thrown value that is not an `Error` is converted with
  `toException` first.

  Barrel order: when a type's initializer (the static constructor body or the static field and
  property initializers, which run inside `#$init`) value-references a class in another file of the
  same barrel that `extends` the type, directly or transitively (`new RoDerived()`,
  `RoDerived.Create()`, `RoDerived.Instance`), the modules form an import cycle that loads only when
  the derived module is evaluated first. The generated `index.ts` barrel then hoists the export
   lines of those derived modules to its top, most-derived first along each inheritance chain
   (#696), with unrelated modules in the barrel's
  existing order; every other line keeps its relative order. Hoisting to the top, not just before
  the base, is needed because any earlier line whose module imports the base evaluates the base
  first. A barrel without such a cycle does not change, and the order is deterministic (NFR-005).
  `[EmitInFile]` keeps declaration order, except that a class that `extends` a class of the same
  file moves directly after its base (today it prints declaration order, which fails at load when
  the derived class comes first); existing goldens do not change.

  Lowering boundary (TypeScript bridge): lazy versus eager is one per-class strategy, chosen by one
   rule (`TypeInitializerLowering.RunsLazily`: a class or struct emitted as a TypeScript class that
   declares a static constructor, plus the derived static-storage exception above), which the
   barrel order consumes too; the eager one is today's
  output. The lazy one
  lowers static fields and auto-properties to slots and accessors, guards the constructor, emits
  `#$init` in place of the static block, and guards static user members in one pass after the class
  members are composed, selected by IR origin and `static` (not by TS name), so operators,
  conversions and overload fast paths added by other lowerings are guarded too. Constructor
  factories are covered by the constructor guard. A lazy struct's promoted auto-properties use
  the same backing-storage/accessor rule as its declared auto-properties (#696). The helper is
  imported as a TypeScript runtime-helper reference, not a core runtime requirement.
  Calls bypassing emitted members (`[Inline]`, `[Emit]`, BCL-mapped static methods,
  and synthesized record `==` / `!=`) still trigger the declaring type's initializer before the
  lowered call or comparison (#696); synthesized metadata and selectors remain non-triggers.
  MS0005 includes value imports referenced only by a static constructor in cycle detection (#696).

  Differs from .NET or not covered (follow-ups):
  - A generic type runs one initializer for all its constructions, since its static members are
    shared; .NET runs one per closed type.
  - A static `async` method runs the guard synchronously, but a failure becomes a rejected promise
    where .NET throws at the call; a static iterator (`static *m()`) runs the guard at the first
    `next()`, not at the call.
  - Where Metano spells a struct's default value as a call of a constructor the struct declares
    (`new S()` for a struct that declares a parameterless constructor, or whose only declared
    constructor TypeScript can call with no arguments, such as `S(params int[] values)`, #291), that
    call runs the guard and the instance initializers, as it runs the initializers of a struct
    without a static constructor.
  - Importing the base module directly (not through the barrel) while its static constructor
    constructs a derived class from another file still evaluates the derived class's `extends`
    clause before the base class exists (`ReferenceError`). Workaround: `[EmitInFile]` or the
    barrel. The same holds when the two modules are re-exported by different barrels. Automatic
    co-location of cycle members is a follow-up.
  - The static constructor body is dropped with no diagnostic in a static class with extension
    members (module-shaped), in a `[Branded]` struct and in an interface. `MS0001` is not extended
    here.
  - A containerless (`[NoContainer]` / `[ExportedAsModule]`) class keeps its `MS0001` error: an ES
    module variable cannot run code when it is read.
  - `[PlainObject]` emits no class; its static members, the static constructor included, are
    dropped (existing).
  - Dart reports `MS0001` (the body is dropped).
  - Existing defects found here, not fixed by this FR: writing a base static through a derived
    name (`Derived.F = 5`) on a type
    without a static constructor creates a shadow property on the derived class; static events
    are emitted as instance members (and so do not trigger it); on a type without a static
    constructor, a private static field `count` and a static property `Count` still clash on the TS
    name `count` (a duplicate declaration).

- **FR-067** (dispatcher order and BCL-virtual groups, #677, extends FR-050): A dispatcher
  MUST try, among overloads of the same tier and arity, the one whose parameters all equal or
  derive from (a base class, an interface, `object`) the matching parameters of another before
  that other, as C#'s binding prefers it (`F(ArgumentException)` before `F(Exception)`); other
  pairs keep their order. An `object` parameter accepts every value in a dispatcher guard. An
  override of a virtual that no emitted class declares (`object.Equals`, `ToString`) and an
  overload of the same name declared beside it form one dispatched group (`equals` dispatcher
  over `equalsOther` / `equalsObj`), where two same-named methods were emitted before; a
  derived override anchors on the first emitted override of its chain. The override of an
  emitted virtual keeps its own group (DECIDED-29). A reference to a local function, which is
  not lowered, reports `MS0001` and prints the throwing placeholder instead of a member call.
  The `System.Numerics.BigInteger` constructors from `int`, `uint`, `long`, `ulong`, `float`,
  `double`, `decimal` and the default one lower to `bigint` values (`BigInt(x)`,
  `bigIntegerFromDouble(x)`, `bigIntegerFromDecimal(x)`, `0n`); the byte-array and span ones are
  not mapped.

  Dart always passes the value. Not filled yet: a transpiled extension method called with
  extension syntax (`x.Ext()`, lowered to a module function), a `base(...)` / `this(...)`
  constructor initializer, an explicit delegate `.Invoke(...)` and an `[Emit]` callee. Traced by the "Caller
  argument expressions" row of the feature-support matrix.

- **FR-068** (JSON `JsonIgnoreCondition.WhenWritingNull`, #667): The generated
  `JsonSerializerContext` MUST leave a `null` member out of the written JSON when System.Text.Json
  would: the context's `DefaultIgnoreCondition = WhenWritingNull`, and a member's own
  `[JsonIgnore(Condition = WhenWritingNull)]` (or `WhenWritingDefault` on a nullable member, whose
  default is `null`), the member's condition taking precedence (`Never` keeps writing it). The
  property spec carries `omitWhenNull: true` and only a nullable member does; reading is
  unaffected. `MS0037` MUST NOT report these; `WhenWritingDefault` on a non-nullable member and a
  context-wide `DefaultIgnoreCondition = WhenWritingDefault` stay reported. Traced by the
  "Naming policies, `JsonSerializerDefaults.Web` and read options" and "STJ member selection" rows
  of the feature-support matrix.

- **FR-069** (indexer reads under `noUncheckedIndexedAccess`, #670): The TypeScript backend MUST
  emit an array, `List<T>` or `string` element read so that the generated code type-checks under
  `strict` with `noUncheckedIndexedAccess`: `a[i]!` for a `T` element and `(a[i] ?? null)` for a
  nullable one, never an assertion that removes the element's own `null`; a write target stays
  assignable (`a[i] = v`, `a[i] ??= v`; `a[i]! += v` and `a[i]!++` for operations that read it
  first); the final step of a native optional chain stays unchecked. A `Dictionary` read is not
  covered. Traced by the "Indexer reads" row of the feature-support matrix.

- **FR-070** (Dart `[StringEnum]` shape, #677): The Dart backend MUST emit a `[StringEnum]` as an
  enhanced enum whose members keep their C# names as identifiers (camelCase), carry the string
  value (a Dart-specific `[Name(Dart, "...")]`, else the `[Name]` value, else the C# name) and
  whose enum declares `final String value;` and `const E(this.value);`; a numeric enum declares
  neither. Traced by the "String enums" row of the feature-support matrix.

- **FR-071** (`char` as a UTF-16 code unit in numeric positions, #501): A `char` keeps its
  representation, a one-unit `string` (TypeScript) and `String` (Dart); this is not a change of
  type, so JSON, dictionary keys, `switch` on a `char`, equality, concatenation and `ToString` are
  unchanged. The numeric reading is explicit, decided from the conversion the semantic model
  reports, never from the operator. The rule: every conversion whose source is `char` and whose
  target has a numeric endpoint (`NumericEndpoints.Of`: `byte`, `sbyte`, `short`, `ushort`, `int`,
  `uint`, `long`, `ulong`, `nint`, `nuint`, `float`, `double`, `decimal`, `BigInteger`, and the
  nullable forms) becomes an `IrNumericConversion` out of `char`. It holds in both gates of the
  core: the implicit one (`NeedsImplicitNode`, reached through `WithImplicitNumericConversion`
  and the coalesce operand) and the explicit one (`IsLoweredExplicitly`: casts, `foreach`
  variables, tuple elements, collection spreads in `IrStatementExtractor` and
  `TupleConversions`). The core always emits the neutral nodes and does not fold a `char`
  literal to an integer; the existing core fold of a `char` literal read as `double` or `float`
  (`double d = 'a'`, `CharLiteralAsFloatingPoint`, which Dart relies on) stays as it is. The
  TypeScript bridge decides the shape and folds the integer-target literals. An enum endpoint (`(E)c`, `(char)e`) is not
  covered (known gap).
  The TypeScript bridge renders such a node as `value.charCodeAt(0)` (narrowed like a `ushort`
  for a narrower target, checked when the context is), and folds the node of a `char` literal to
  its code (`'a'` read as a number is `97`). That covers `int i = c`, `(int)c`, `(ushort)c`,
  `Math.Max(c1, c2)` (binds `ushort`), `c + 1`, `c - 'a'`, `c * 2`, `-c`, `+c`, `~c`, shift counts
  (`l << c`), `long r = c + i` (`BigInt(c.charCodeAt(0) + i)`), `decimal d = c` (a `Decimal`),
  `c < n`, `c == 97`, `f ? c : 0`, a lambda return typed `int`, an argument to an `M(int)` /
  `M(string)` overload set (the conversion lets the runtime dispatcher see a `number`),
  `foreach (int x in str)`, `int[] a = [..str]` and tuple deconstruction into `int`.
  A `char` literal whose converted type is numeric folds the same way wherever it appears,
  including the constants of a switch or pattern over an integer (`switch (i) { case 'b': }`,
  `i is 'a'`, `i is >= 'a' and <= 'z'`, `i == 'a'` are `98`, `97`, ... in the integer test).
  The bridge drops the `charCodeAt` when both operands of a relational or equality operator are a
  non-nullable `char` (`c == 'a'`, `'a' == c`, `c >= 'a'`, `a == b`) and keeps the one-unit
  strings compared: a JS string comparison orders by UTF-16 code unit, as .NET orders `char`; this
  exemption runs on the conversion nodes, so Roslyn's int binding of the operator never folds
  the literal to a number. Patterns over a `char` (`c is >= 'a' and <= 'z'`, `switch (c)` with
  `char` constants) stay string tests. A nullable operand (`char?`) does not take the
  exemption: it goes through the lifted conversion (`n == null ? null : n.charCodeAt(0)`, as
  FR-019 of spec 012) and the existing lifted relational lowering, so `n + 1`, `(int?)n` and
  `n < 5` follow C# (`null <= null` is `false`, where comparing strings in JS would say `true`).
  Text positions stay text: `"x" + c`, `c + "x"`, `$"{c}"`, `c.ToString()` and `new string(c, n)`
  never read the code. An arithmetic result is an `int` (a `number`, or a `bigint` once widened to
  `long`): `c + 1` is not a `char`.
  Range: a `char` is the integer range `[0, 0xFFFF]` for the integer range analysis that decides
  where an unchecked `int` operation needs a 32-bit wrap (#639, layer 3). This feature does not
  add the `char` arm to `IntegerValueRanges` (it would drop a Dart `.toSigned(32)`, and Dart
  output does not change); #639 adds it with its own Dart handling. Order of the three steps of
  an expression such as `(char)(c + 1)`: the `char` to `int` node on the operand, the `int`
  overflow wrap on the `int` result (when the analysis asks for it), the `int` to `char` store
  narrowing last. Whichever of this work and #639 lands second regenerates the goldens the other
  changes.
  Dart: no output changes. The target keeps every `char` shape it prints today, wrong ones
  included (`int x = c`, `c + 1`, `c++`, `c += (char)1` print as before; `(char)98` stays
  `98 as String`; a `foreach` over numbers into a `char` variable keeps its form): the core
  lowers a `char` as its code only when the target says so (`NumericEndpoints.LowersCharCodes`,
  false for Dart), the way a tuple conversion is kept unconverted for Dart. The existing fold of
  a `char` literal read as `double` or `float` is unchanged. Char arithmetic in Dart is a
  documented gap, with no `codeUnitAt`. The Dart outputs of the conformance probes and of the
  sample regeneration stay byte for byte. The only Dart text FR-073 changes is a lone surrogate,
  whose literal made the CLI abort before.

- **FR-072** (`char` stores and narrowing, #501): A `char` is produced from a number only
  through `String.fromCharCode`, keeping the low 16 bits as .NET does. `(char)n` and
  `(char)(c + 1)` are `String.fromCharCode(n & 0xFFFF)` for an `int` source; a `long`,
  `BigInteger` and floating-point source keep the existing lowering of spec 012 (FR-011, FR-070).
  An increment, a decrement or a compound assignment whose target is a `char` (`c++`, `--c`,
  `c += (char)2`, `c -= (char)1`; `c += 1` is CS0266 in C#) is modelled in the IR as the store
  `int` to `char` of (`char` to `int` of `c`) `+/-` the operand, with no TypeScript text in the
  core: it reuses the read-modify-write lowering of `IrExpressionExtractor.NullableIncrement.cs`
  (`WriteIncrement`: `BindTargetOnce` and `WriteAndYieldPrevious`, with `IsValueUnused` covering
  statements and `for` incrementors), so the postfix form with a used value and a target that
  must not be evaluated twice (`a[i++]++`, a property) are supported, not reported. The store
  follows Roslyn's `IsChecked`, including a project with `CheckForOverflowUnderflow`:
  unchecked wraps (`'\uffff'++` is `0`), checked throws `OverflowException` through the existing
  checked-narrow helper (`checked { c++; }` and `checked { c += (char)1; }` at `'\uffff'` throw
  in .NET). The reduction reuses `IntegerOverflowLowering.ReductionOf` and
  `TryExtractReducedIncrement`, which already handle `byte`. A `foreach` variable declared `char`
  over numbers (`foreach (char x in doubles)`) converts each element with these rules and is a
  one-unit string, so `t += x` on an `int` reads its code (FR-071) instead of concatenating.
  Dart: as the Dart paragraph of FR-071 (the stores keep their current Dart shapes).

- **FR-073** (lone surrogates in literals, #501): No generated file MUST fail to write because
  of its text. The TypeScript printer MUST emit an unpaired UTF-16 surrogate of any string
  literal, `char` literal or template quasi as the `\uXXXX` escape (`'\uD83D'` prints as
  `"\ud83d"`), so the file is valid UTF-8 and the literal evaluates to the same code unit. A
  valid pair stays printed as its character, so existing output is unchanged; telling a pair
  from a lone surrogate needs the neighbouring unit, so the escape works on the string with an
  index, not on a unit at a time (`EscapeNonDelimiterChar`). JSX text and JSX attribute values,
  which cannot hold a backslash escape, print a lone surrogate as a JS expression
  (`{"\ud83d"}`). The Dart backend escapes a lone surrogate the same way (`\uD83D` in a Dart
  string, with the same pair detection, not the `Replace` chain of `DartStringLiteral`) and is
  otherwise unchanged. As a backstop, `TranspilerHost.EmitFilesAsync` catches an
  `EncoderFallbackException` of a file and reports `MS0001` (the existing "not supported" code;
  no new code) naming the file and the offending index, so the run does not abort mid-loop;
  the same text a printer later forgets to escape then fails loudly. Before this requirement
  the CLI aborted with the unhandled exception. Traced by the "`char` as a code unit" and "Lone
  surrogate literals" rows of the feature-support matrix.

  Known gaps of this feature, not designed here: `(char)e` / `(E)c` with an enum endpoint, the
  redundant `& 0xFFFF` in a `fromCharCode` argument, an `M(char)` / `M(string)` overload pair
  (indistinguishable at runtime), `[Queryable]` expression trees over `char`, `char.IsDigit`,
  `c.CompareTo`, `new string(c, n)` and `new string(char[])` (the BCL mappings do not resolve
  them), a nullable `char` increment or compound assignment (`n++`, `n += x` on a `char?`
  concatenates), `(char)decimalValue` (stores the `Decimal`), `int max = char.MaxValue` (prints
  `Char.maxValue.charCodeAt(0)`, a ReferenceError), `int[] arr = { c, 'b' }` (an unsupported
  array initializer), and the Dart `'\0'` literal printing a raw NUL.

- **FR-074** (`int` / `uint` arithmetic wraps in the default context, #639 layer 3; completes
  spec 012 section K): In an unchecked context (Roslyn's `IsChecked` is false: no `checked`
  scope and no `CheckForOverflowUnderflow`, 012 FR-023), the result of the built-in `+`, `-` and
  `*` computed in `int` or `uint` and of unary `-` on an `int` (operands narrower than `int`
  included, since C# promotes them; unary `-` on a `uint` is promoted to `long` and follows the
  64-bit rules), and the value that `++`, `--`, `+=`, `-=` and `*=` store in an `int` / `uint`
  local, field, property, array element or indexer, MUST be reduced modulo 2^32 to the type's
  range, as .NET wraps it (`int.MaxValue + 1` is `int.MinValue`, `0u - 1u` is `4294967295`).
  The core decides it from Roslyn and represents it as 012 FR-026's
  `IrIntegerOverflow(Value, Int32 | UInt32, Wrap)` for a target whose numeric lowering rules
  ask for it (`NumericLoweringRules.WrapsInt32Arithmetic`, the policy record that also holds the
  `char` rules of FR-071): TypeScript turns it on, Dart keeps it off; an increment or a compound assignment stores
  the reduced value through 012 FR-024's read-modify-write (receiver and index evaluated once; a
  postfix form whose value is used yields the stored value stepped back and reduced again, with
  no temporary), and a lifted form reduces its non-null result (012 FR-025). The TypeScript
  bridge renders the wrap with native operators and imports nothing:

  | C# (unchecked) | TypeScript |
  | --- | --- |
  | `a + b`, `a - b`, `-a` on `int` | `(a + b) \| 0`, `(a - b) \| 0`, `-a \| 0` |
  | `a + b`, `a - b` on `uint` | `(a + b) >>> 0`, `(a - b) >>> 0` |
  | `a * b` on `int` / `uint` | `Math.imul(a, b)` / `Math.imul(a, b) >>> 0` |
  | `i++;`, `i += k;`, `u *= k;` | `i = (i + 1) \| 0;`, `i = (i + k) \| 0;`, `u = Math.imul(u, k) >>> 0;` |
  | `x = i++` | `x = ((i = (i + 1) \| 0) - 1) \| 0` |

  A product MUST use `Math.imul`, never `(a * b) | 0`: two 32-bit factors can exceed 2^53, where
  a `double` has lost the low bits. **Dart is out of scope** (maintainer decision of
  2026-11-17): its output MUST stay byte-identical, with no `.toSigned(32)` / `.toUnsigned(32)`
  for this requirement and no MS0001 on a dictionary entry, so unchecked `int` / `uint`
  arithmetic keeps the 64-bit Dart `int` result (`int.MaxValue + 1` is `2147483648`), a
  documented gap deferred to after v3 (a later wrap would print DECIDED-19's selectors, and
  would need a `Math.imul` counterpart on Dart web, where an `int` is a `double`). Not reduced: `/` and `%` (only `int.MinValue / -1` and `% -1` leave the
  range, and .NET throws `OverflowException` for them in both contexts: the divergence of
  baseline FR-014 stays), an operation whose result is of an enum type (`e + 1` on an enum `E` is
  an `E`; an operation on enum operands whose result is an integer, such as
  `DayOfWeek.Friday - DayOfWeek.Monday`, which is an `int`, is reduced), user-defined operators,
  `[Queryable]` expression trees, and constants (Roslyn folds them, 012 FR-074). `char`
  arithmetic is typed `int` by C# and is reduced like any `int` result; a `char` operand is a
  number in `[0, 0xFFFF]` (#501), so `c + 1` and `c - 'a'` fit and `c * 70000` wraps. In TypeScript, a reduced
  non-lifted store on a dictionary entry (`counts[key]++`, `counts[key] += n` on a `Dictionary<_, int>`)
  MUST report `MS0001` as an error, so the build fails as it did before this requirement, when
  `tsc` rejected the invalid `counts.get(key)++` it emitted; the dictionary-entry stores
  reported before it (checked forms, types narrower than `int`, shifts and `uint` bitwise
  reads, 012 Edge Cases) keep their warning on every target. This closes the known gap of 012
  section K: an unchecked result read by checked code (`checked(a + unchecked(b * c))`, a
  `var h = unchecked(a * 31 + 7); h + 1` in a checked project) is in range, and a cast to the
  operand's own type (`(int)(i + j)`) reads the wrapped value. Traced by the "`int` / `uint`
  wrap-around" row of the feature-support matrix.

- **FR-075** (wraps the core proves unneeded, #639): The core MUST omit the reduction of FR-074,
  and the check of 012 FR-077 (one rule for both contexts), where C# shows that the exact result
  fits the type. The proofs are neutral facts the core computes from Roslyn; whether a target
  reads the new ones is its numeric lowering policy (`NumericLoweringRules`): TypeScript reads
  them all. **Dart is deferred** (after v3, with FR-074): it reads no `char` range
  (`LowersCharCodes` is off) and neither the `Length` / `Count` ranges nor the bounded step
  (`ReadsCollectionAndLoopRanges` is off), so its output is byte-identical (`(uint)s.Length`
  keeps `.toUnsigned(32)`, `(uint)(c + 1)` keeps its narrowing, a checked bounded step keeps its
  read-modify-write); the operand ranges of 012 FR-074 that predate this requirement apply to
  every target as before:
  - the operand ranges of 012 FR-074 (`IntegerValueRanges`: a constant, a value widened from a
    narrower type, a mask; `b1 + b2` on `byte`, `s * 2` on `short`, `(x & 0xFF) + 1`), which
    also read as `[0, int.MaxValue]` the `Length` of an array or a `string` and the `Count` of
    these BCL classes, matched by symbol identity (`OriginalDefinition`), which own their
    storage: `List<T>`, `Dictionary<TKey, TValue>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`,
    `SortedDictionary<TKey, TValue>`, `SortedList<TKey, TValue>`, `SortedSet<T>` and
    `LinkedList<T>`. Any other `Count` spans its type (`Collection<T>`,
    `ReadOnlyCollection<T>` and `ObservableCollection<T>` read it from a wrapped user `IList<T>`,
    and an interface may return anything). So `a.Length - 1` emits `a.length - 1`;
  - the step of a counting loop: `i++` / `++i` (`i--` / `--i`) as an incrementor of a `for`
    whose variable `i` (`int` or `uint`, or a numeric enum with either underlying endpoint,
    #708) is declared in the `for` initializer and is not written
    in the condition, the body or the other incrementors (Roslyn data flow, `ref` / `out`
    arguments and lambdas included), when the condition is a comparison that bounds the step,
    or such a comparison is a top-level operand of a `&&` chain that is the condition (a
    comparison nested in any other expression, such as `(i < 0) == false` or `!(i >= n)`, does
    not count). The comparison is computed in `i`'s own type and one of its operands is the
    local `i` itself with no conversion (`(short)i < 100` does not bound `i`): `i < e` (or
    `e > i`) for `++`, `i > e` (or `e < i`) for `--`, `i <= e` when `e`'s range ends below the
    type's maximum, and `i >= e` when `e`'s range starts above the type's minimum (`i >= 0` on an
    `int`, never on a `uint`). The step keeps the native operator:
    `for (let i = 0; i < n; i++)`. Tests MUST pin the negative shapes (a nested comparison, a
    converted counter, a comparison promoted to `long`, a counter written in the body or passed
    by `ref`), which keep the wrap.

- **FR-076** (TypeScript consumers that already reduce, #639): The TypeScript bridge MUST omit
  the final `| 0` / `>>> 0` of an `int` / `uint` wrap (FR-074, and 012 FR-076's reads) on an
  operand whose JavaScript consumer reduces it modulo 2^32 itself: the operand of another wrapped
  `int` / `uint` `+`, `-` or unary `-`, of `Math.imul`, of `&`, `|`, `^`, `~`, a shift, or of an
  unchecked 012 FR-074 narrowing conversion that emits its reduction (a checked narrowing reads
  the exact value and keeps the inner wrap; a conversion the ranges drop reduces nothing),
  recursively through the operands of `+`, `-` and unary `-`. Only the trailing reduction goes:
  a wrapped `int` product keeps `Math.imul` (`(a * b) + c` emits `(Math.imul(a, b) + c) | 0`,
  never `(a * b + c) | 0`, which gives `0` instead of `18` at `a = 0x7FFFFFFF`,
  `b = 0x7FFFFFF3`, `c = 5`), and a `uint` product drops only its `>>> 0`
  (`(Math.imul(a, b) + 1) >>> 0`). The unreduced operand is exact (a sum of n values of 32 bits
  stays below n · 2^32, far inside 2^53, and `Math.imul` returns 32 bits), and the consumer
  applies `ToInt32` / `ToUint32` to it, so `a + b + c - a * b` emits
  `(a + b + c - Math.imul(a, b)) | 0` and `(a + b) & 0xFF` stays as written. The printer keeps the
  grouping the omitted wrap supplied: `a - -b` never prints as `a--b`, and `a - (b + c)`,
  `a - (b - c)` and `-(a + 1) | 0` keep their parentheses; goldens MUST pin these shapes and the
  product case above. Every other consumer keeps the wrap: a checked operation (`checkedInt32`),
  a comparison, `/`, `%`, a conversion to `double`, `long` (`BigInt(...)`) or `string`, an
  argument, a return value and a store. Dart has no FR-074 wrap to elide (out of scope, see
  FR-074).

  Known gaps and follow-ups of FR-074–FR-076: a real read-modify-write on dictionary entries
  (instead of `MS0001`); the range of a proven counter inside the loop body (it spans its type
  there); a write through `Unsafe.AsRef(in i)`, which escapes the data-flow proof of FR-075; the
  asm.js-style `((s | 0) + x) | 0`, which avoids a V8 slow path for an accumulator that
  overflows on most iterations; `int.MinValue / -1`, `% -1` and `Math.Abs(int.MinValue)`; and
  the Dart wrap (FR-074).

  Benchmark (recorded for the maintainer decision; Apple M1 Pro, Bun 1.4.2, Node 24.11.0; median
  of 7 runs of a 5·10^7-iteration loop over an `Int32Array`, in ms):

  | Loop body | Bun | Node |
  | --- | --- | --- |
  | sum in range, unwrapped (before FR-074): `s = s + x` | 109 | 80 |
  | sum in range, wrapped: `s = (s + x) \| 0` | 95 | 48 |
  | hash, unwrapped: `h = h * 31 + x` | 219 | 159 |
  | hash, wrapped: `h = (Math.imul(h, 31) + x) \| 0` | 140 | 76 |
  | accumulator overflowing on most iterations, wrapped: `s = (s + x) \| 0` | 48 | 415 |
  | the same, asm.js form: `s = ((s \| 0) + x) \| 0` | 51 | 39 |
  | the same on `uint`: `s = (s + x) >>> 0` | 121 | 407 |

  In range, the wrap is as fast as or faster than the unwrapped `number` (V8 and JavaScriptCore
  keep the value a small integer), and a wrapped hash is faster than the unwrapped one, which
  leaves the integer range. An accumulator that overflows on most iterations hits a V8 slow path
  on Node (about 10 times the asm.js form; Bun has none). The maintainer accepted the Node slow
  path for 3.0 (2026-11-17); the asm.js form stays a follow-up.

- **FR-077** (user-defined operators at their use sites, #599): Every application of a
  user-defined operator that C# binds (binary, unary, relational, `==` / `!=`, `++` / `--` and
  the compound assignment `x op= y`), on a class, struct or record and declared on either
  operand's type, MUST run that operator. The core extracts it as its own neutral node,
  `IrUserOperatorApplication(Op, BoundOperator, Operands, IsLifted)`, never as an
  `IrBinaryExpression` / `IrUnaryExpression` with an optional field nor as an
  `IrCallExpression`, so no core lowering of built-in arithmetic (the integer overflow
  reduction, the integer value ranges, decimal and Temporal arithmetic) mistakes it for a
  built-in operator and wraps or rewrites it. `BoundOperator` is a record of neutral facts: the declaring type, the operator's
  C# metadata name, the overload of a dispatched group when there is one, and its `[Name]`
  pin. The operands are Roslyn's, with their conversions (FR-079, FR-080, spec 012's numeric
  ones). The core's TypeScript-specific `"$" + opName` compound rewrite and the
  `EmitsOperatorFastPaths` gate are removed: each target decides the shape. TypeScript calls
  the declaring type's static helper of FR-078 (`Tick.__add(a, b)`, `Pt.__negate(p)`,
  `Meters.__add(5, m)`), the fast path of a dispatched group as spec 013 FR-018 already does
  (`Vec.__multiplyDoubleVec(2, v)`), and the group's dispatcher (`Amount.__multiply(a, 3)`)
  for a type read from a metadata-only reference (spec 013 DECIDED-24). `x op= y` stores
  `Out(T.__op(In(x), y))`, with Roslyn's `InConversion` / `OutConversion` of the compound
  operation (either may be user-defined: `sv += 2` with `Feet operator +(S, int)` and
  `implicit operator S(Feet)` stores the `Feet` → `S` conversion of `sv + 2`'s result), and `x++` / `++x` store
  `T.__increment(x)`, both through the read-modify-write that already binds a target's
  receiver and index once (`IrExpressionExtractor.ReadModifyWrite.cs` / `WriteIncrement`,
  spec 012 FR-075–FR-077, #497), so `arr[i++] += t` reads `i` once; a postfix form yields the
  value read before the store. A lifted operator over `Nullable<T>` yields `null` when an
  operand is `null` (arithmetic, bitwise, unary) or `false` (relational); a lifted `==` is
  `true` when both operands are `null`, `false` when one is, and the operator's result
  otherwise, and `!=` the opposite; each operand is evaluated once. An `==` / `!=` with an
  operand whose constant value is `null` (`null`, `default`, `(T)null`) keeps the plain null
  test without calling the operator, so TypeScript keeps narrowing the other operand. For a
  struct or `Nullable<T>` operand that is exactly .NET's lifted semantics; for a class operand
  it is a documented divergence (an operator that treats `null` as a value is not called). A
  record's synthesized `==` / `!=` keeps its value-equality lowering (FR-060); a record cannot
  declare its own. The applications inside the type's own members (an `Equals` written as
  `this == other`) follow the same rule. Not lowered, reporting `MS0001` (warning) at the
  application with the throwing placeholder: a user-defined `true` / `false` operator used by
  a condition, `&&` or `||` (a condition over `implicit operator bool` is FR-079, not this
  case); a C# 14 instance compound-assignment or increment operator (`void operator +=(int k)`,
  `void operator ++()`); an operator of a `[PlainObject]` type, which has no class to declare a
  helper on (also `MS0001` at its declaration). These placeholders are TypeScript's: Dart,
  which prints the user-operator nodes as the source wrote them, keeps these shapes and their
  diagnostics as they were, and keeps the `x = x.$add(y)` form of the removed rewrite for a
  `+=` / `-=` / `*=` / `/=` / `%=` through an operator the core does not apply (a BCL,
  `[External]` or `[PlainObject]` type, an instance operator). Out of scope and unchanged: an operator applied
  to a type parameter through a `static abstract` interface member (generic math,
  `T : INumber<T>`, `a + b`), which keeps today's raw `a + b` (it runs under Bun for numbers; a
  placeholder would regress it). Known limitation: a struct operator that mutates its
  parameter (`operator ++(S s) { s.V++; return s; }`) aliases in TypeScript, where structs are
  references, so the postfix value is the new one (.NET: old `5`, stored `6`; TypeScript:
  `6` / `6`). Dart output MUST stay byte-identical (maintainer rule for wave 2), including the
  shapes that are wrong today: the Dart bridge renders the new node back to today's Dart text —
  its native operator at every binary, unary, relational and equality application, `t =
  t.$add(y)` at a compound assignment and `c++` at an increment — by itself, never through a
  target decision in the core. Because Dart prints the node as its native operator,
  `DartBinaryOperandGrouping` MUST group it exactly like the equivalent `IrBinaryExpression` /
  `IrUnaryExpression`, as the outer operand and as the inner one (precedence keyed on the
  operator, not on the node type): `(a + b) * 3` with a user `+` keeps its parentheses, where
  the grouping that only knows `IrBinaryExpression` would print `a + b * 3`. A Dart golden pins
  today's output for a mixed user / built-in expression, a compound assignment and an
  increment. A shape whose Dart text cannot stay byte-identical is reported to the maintainer
  before it lands.
  Follow-ups (gaps, not this requirement): Dart's operators declared for the right operand and
  a user `!=` that Dart ignores. Traced by the "User-defined operators at their use sites" row
  of the feature-support matrix.

- **FR-078** (operator and conversion helpers, #599, #600; amends spec 013 FR-005 and its naming
  contract): Each user-defined operator MUST declare a static helper on its type named `__` +
  the operator's name: `add`, `subtract`, `multiply`, `divide`, `modulo`, `equals`,
  `notEquals`, `lessThan`, `greaterThan`, `lessThanOrEqual`, `greaterThanOrEqual`, `bitwiseAnd`,
  `bitwiseOr`, `xor`, `shiftLeft`, `shiftRight`, `shiftRightUnsigned`, `not`, `bitwiseNot`,
  `negate` (unary `-`), `plus` (unary `+`), `increment`, `decrement`, and `checked` + the
  capitalized name for a `checked` operator (`__checkedAdd`). Unary `-` and `+` took the binary
  names before (`__subtract`, `__add`), so a type declaring both forms of `-` emitted two
  `__subtract` members (TS2393). `[Name]` renames a single operator; a dispatched operator group
  keeps spec 013 (fast paths `__addMoneyInt32`, `[Name]` pins a suffix). The instance helper
  (`$add(b)`, or the instance dispatcher of a group) covers only the operators whose first
  parameter is the declaring type or its nullable form: an operator declared for its right
  operand (`operator +(int a, Meters m)`) has no instance form, where today `$add(m)` passes
  `this` as the `int` (TS2345). A `true` / `false` operator declares nothing. Each conversion
  operator MUST declare its own static, never a dispatcher, because C# selects a conversion by
  its source and target types, never by its arguments: `__to` + the target type's spelling for an
  explicit conversion out of the declaring type, `__from` + the source type's spelling for one
  into it, `__implicitTo` / `__implicitFrom` for an implicit conversion, and `__checkedTo` /
  `__checkedFrom` for a `checked` conversion operator, which is always explicit and which C#
  binds in a checked context beside its unchecked twin (`explicit operator int(Money m)` →
  `__toInt32`, `explicit operator checked int(Money m)` → `__checkedToInt32`,
  `explicit operator long(Money m)` → `__toInt64`, `implicit operator Money(int c)` →
  `__implicitFromInt32`, `implicit operator string(Money m)` → `__implicitToString`,
  `implicit operator Cents?(int? v)` → `__implicitFromInt32OrNull`; maintainer decision D1).
  C# forbids an implicit and an explicit conversion between the same two types (CS0557), so
  the kind never tells two conversions apart; it is in the name so a reader sees it, and
  switching a conversion between implicit and explicit renames its TypeScript static (a 3.0
  API note). The spellings are spec 013's CLR spellings (DECIDED-3), the rule its method
  suffixes follow: a converted type's `[Name]` does not change them
  (`[Name("Cash")] class Money`: `implicit operator Wallet(Money m)` → `__implicitFromMoney`,
  `explicit operator Money?(Wallet w)` → `__toMoneyOrNull`; maintainer decision), and nested
  type arguments stay CLR (`__toListOfMoney`). A `[Name]` on the conversion operator itself
  still names it whole. A conversion group's overload suffix is the same candidate for the same
  target, so the core holds one name per conversion.
  The core keeps grouping conversion operators as it does today (`MethodOverloadGroups` is not
  restructured) and adds one neutral fact per conversion declaration,
  `IrConversionName { Direction (To / From), Kind (Implicit / Explicit / Checked), Spelling, Pin }` on
  `IrMethodSemantics`, computed by one function from the Roslyn symbol
  (`ConversionNaming.Of(IMethodSymbol)`), which the use sites of FR-079 call too. Two
  conversions of one type whose candidates collide climb with `OverloadSpellingClimb` as spec
  013 step 5 describes, and a dead end is spec 013's `MS0003`. The old conversion suffix rule
  (`OverloadSignature.ConversionTarget`, `MoneyToInt32`) is removed: a conversion group gets no
  overload suffix, so there is one rule for the concept (Dart names the primary by `[Name]` or
  its metadata name, never by that suffix). The TypeScript bridge un-groups the conversions and
  emits one static each; the `__` prefix, the lower-casing and the claim stay in the bridge, as
  for spec 013's names. A conversion name claimed by another member the type itself declares
  is an `MS0003` error at the conversion's declaration; inherited statics do not claim it
  (`implicit operator Base(int)` on `Base` and `implicit operator Der(int)` on `Der : Base` are
  both `__implicitFromInt32`) (no `$` rename): a static, or an operator's instance form, that
  a derived class declares under a name its emitted base declares on the same side is emitted
  `override` (`static override __implicitFromInt32`, `static override __add`,
  `override $add`; TS4114 under `noImplicitOverride`), the base read from metadata alike. So a conversion's name is a pure
  function of its symbol and a metadata-only reference computes the same one without running
  the producer's claim pass. `[Name("toInt")]` names that one conversion (`__toInt`), no longer
  its group. Every conversion is declared, so one to a type the target neither emits nor maps
  (`implicit operator System.Text.StringBuilder(Vec v)` →
  `__implicitToStringBuilder(v: Vec): StringBuilder`) fails `tsc` with `TS2304`, where the old
  rule emitted nothing for it; `[Ignore]` on that conversion declares no static (known
  limitation). A conversion declares no instance helper. The static helpers of a generic type
  re-declare the type's type parameters with their constraints, as spec 013 FR-028 does for
  factories (`static __add<T>(b: Box<T>, n: number): Box<T>`,
  `static __toT<T>(b: Box<T>): T`), which fixes today's TS2302. A `[Branded]` /
  `[InlineWrapper]` companion namespace exports the same names as functions (`Qty.__add`,
  `Qty.__implicitFromInt32`, and the per-overload fast paths of an operator group) instead of
  `op_Addition` / `op_Implicit`. Every conversion is declared, the identity unwrap included
  (`implicit operator string(Slug s) => s.Value` is
  `export function __implicitToString(s: Slug): string { return s; }`): a consumer that sees
  the type only through metadata cannot read the body, so it calls the static whatever the
  body is, and producer and consumer agree with no extra fact (maintainer decision, option
  (a)). Today the companion drops a conversion to the underlying primitive by return type
  alone. The operator helpers and conversion
  statics of a class flow out through the class emitter's member composition
  (`EmitDeferredOperatorsAndMethods` / `EmitOperator`), so the lazy static-constructor guard of
  #681 applies to them; companion functions do not pass through it (a `[Branded]` struct with a
  static constructor is #681's documented gap). Dart keeps its declarations (byte-identical):
  one static per operator and the primary of a conversion group with its `MS0001`. Traced by
  the "User-defined operators at their use sites" and "User-defined conversions" rows of the
  feature-support matrix.

- **FR-079** (user-defined conversions at their use sites, #599): Every user-defined conversion
  C# applies MUST run its conversion operator: implicit ones (an initializer or assignment, an
  argument, a `return`, either operand of `??`, a conditional branch, an element of a
  collection, array or tuple, an operand of an operator or of a string concatenation, a
  condition through `implicit operator bool`: `if`, `while`, `?:`, `!b`, and the operands of
  `&&` / `||` when the type declares no `&` / `|` / `true` / `false` operator, the in and out
  conversions of a compound assignment, and a `foreach` iteration variable, whose conversion
  Roslyn reports as `ForEachLoopOperationInfo.ElementConversion`), and explicit ones (a cast).
  The core extracts it as its own neutral node,
  `IrUserConversion(BoundOperator, Operand, Before: IrNumericConversion?, After: IrNumericConversion?, IsLifted)`,
  where `Before` converts the source to the operator's parameter and `After` the operator's
  result to the target type, each present where spec 012 FR-010, FR-071 (the `char`
  endpoints) or FR-080 needs a node, and `BoundOperator` is FR-077's record with the
  `IrConversionName` of FR-078. TypeScript calls the static of FR-078 on the declaring type
  (`Money m = 7` → `Money.__implicitFromInt32(7)`; `Money m = 'a'` →
  `Money.__implicitFromInt32(97)`; `(long)m` → `Money.__toInt64(m)`; `string s = m` →
  `Money.__implicitToString(m)`; `long l = tick` → `BigInt(Tick.__implicitToInt32(tick))`;
  `decimal e = new Cents(1) + 1m` →
  `Cents.__implicitToDecimal(new Cents(1)).plus(new Decimal("1"))`;
  `foreach (Money m in ints)` → `const m = Money.__implicitFromInt32(item)` per element). A lifted
  conversion (`Cents? c; decimal? d = c`) yields `null` for `null`
  (`c == null ? null : Cents.__implicitToDecimal(c)`), evaluating the operand once. An explicit cast of
  a nullable source over an operator that takes the non-nullable type behaves as .NET: to a
  struct target (`(N)intNullable`) a `null` throws `InvalidOperationException` ("Nullable
  object must have a value."), and to a class target (`(Money)intNullable`) a `null` yields
  `null` without calling the operator; TypeScript never calls `N.__fromInt32(null)` (or its implicit form). The
  reference rules are FR-077's, and a metadata-only reference names the same static, since its
  name is a function of the symbol (FR-078). An interpolation hole formats its value
  (`ToString`) without a conversion and is unchanged. A conversion of a branded type calls its
  companion function, the identity unwrap of FR-078 included (`string s = slug` →
  `Slug.__implicitToString(slug)`), in the same compilation and through metadata alike. A conversion
  involving `dynamic` is left to spec 011 FR-030. Dart renders the node as today: the operand
  of an implicit conversion and `operand as T` for a cast (byte-identical). Follow-ups (gaps):
  Dart `MS0001` for the conversions it erases; the arguments of a collection or object
  initializer's `Add`; a checked `Before` conversion (`(Money)someLong` over an operator
  taking `int`), which needs spec 012's checked narrowing. Traced by the "User-defined
  conversions" row of the feature-support matrix.

  New-node checklist for `IrUserOperatorApplication` and `IrUserConversion` (FR-077, FR-079):
  every IR walker and rewriter handles both — the JSX component bridge's rewrite
  (`IrToTsJsxComponentBridge`), the `[PlainObject]` `RewriteThis` (`IrToTsPlainObjectBridge`),
  the TypeScript `ImportCollector` walkers (the declaring type's import), the runtime
  requirement scanner (`IrRuntimeRequirementScanner`), the body coverage probe, the Dart
  printer, `DartBinaryOperandGrouping` and `DartIrRewriter`.

  As landed (G3c): the nodes live in `IR/IrUserOperators.cs`. `BoundOperator` is
  `IrUserOperator(DeclaringType, MetadataName, Overload, Pin, ConversionName)`. A compound
  assignment or increment is a third node, `IrUserOperatorStore(SourceForm, ReadModifyWrite)`:
  the read-modify-write of FR-077 beside the store as the source wrote it (`t += y`, `c++`).
  `IrUserConversion` is `(Operator, Operand, After, IsLifted, SourceForm)`: `Before` is the
  numeric conversion node applied to `Operand`, `After` an `IrNumericStep` (the endpoints of the
  conversion node it stands for), and `IsLifted` also covers an explicit cast from a nullable
  value to a class. Each node carries its source form, and Dart prints that form (the Dart
  rewriter turns a compound `+=` / `-=` / `*=` / `/=` / `%=` into `t = t.$add(y)` and the
  `foreach` variable declared from a conversion back into the loop variable), so its output is
  byte-identical, today's double evaluation of `arr[i++] = arr[i++].$add(m)` included;
  `DartBinaryOperandGrouping` sees the native operators. The TypeScript import of the declaring
  type comes from the type reference the call names, so `ImportCollector` and the runtime
  requirement scanner need no case. The `EmitsOperatorFastPaths` port and
  `TryExtractUserOperatorCall` are removed. A `[JsxComponentBuilder]` family's conversion to its
  element (the JSX rendering protocol) keeps its lowering. A condition tested through a user
  `true` operator (`if (x)`, `x ? a : b`) reports `MS0001` like `&&` / `||`. A cast whose user
  conversion is followed by a numeric one (`(byte)zpt` over `explicit operator int(ZPt)`,
  `(int)wide` over `implicit operator long(ZWide)`) runs the operator and then the explicit
  numeric conversion (`ZPt.__toInt32(zpt) & 0xFF`, checked in a checked context); Roslyn reports
  no operation for such a cast, so its conversions are read from the enclosing operation. Not covered
  (follow-ups): a user conversion of a tuple element, and the arguments of a collection
  initializer's `Add` (as FR-079 lists).

- **FR-080** (implicit integer-to-decimal conversions, #599 follow-up, #476): An implicit
  conversion from `sbyte`, `byte`, `short`, `ushort`, `int` or `uint` to `decimal`, or between
  their nullable forms, MUST become spec 012's numeric conversion node, through the one rule
  that already decides the 64-bit ones: an `IsIntegerToDecimal` clause in
  `NumericLoweringRules.NeedsImplicitNode` and `IsLoweredExplicitly`, next to the `char` clause
  of FR-071, gated by the target fact `NumericLoweringRules.ComputesDecimalByMethods` (a target that
  computes with a `decimal` as a runtime value whose operators are methods; false for Dart).
  It applies wherever
  Roslyn reports the conversion: an initializer or assignment, an argument, a `return`, an
  operand of an arithmetic, relational or compound operator, and the left operand of `??`
  whose value C# converts to a `decimal` result (`int? a = null; decimal d = a ?? 1.5m`,
  `decimal e = n ?? 0m` with `n` an `int?`). The explicit cast `(decimal)i` from the same
  sources lowers the same way (`IsLoweredExplicitly`). TypeScript emits `new Decimal(x)`, exact
  for every value of these types, and lifted `x == null ? null : new Decimal(x)`; today the
  `number` reaches the `Decimal` slot unchanged and the first `Decimal` method call on it
  throws `TypeError`. `long` / `ulong` keep their path (`new Decimal(x.toString())`). Not
  covered: `char` (#501), and the explicit `float` / `double` casts, which .NET rounds to 7 / 15
  significant digits. The same target fact makes the stores into a `decimal` call its methods:
  a compound assignment (`d += i` → `d = d.plus(new Decimal(i))`, `-=`, `*=`, `/=`, `%=`) and
  `++` / `--` (`d = d.plus(new Decimal("1"))`, a used postfix value yielding the value read
  before the store) go through the read-modify-write that binds the target's receiver and index
  once; today the native operator coerces the `Decimal` to a string or a number (`a += d`
  concatenated, `a++` stored a `number`). Not covered (known gap): `++` / `--` on a `decimal`
  extension property or extension indexer, which keep their own lowering. A JS integer can
  hold `-0` (`-4 % 2`, `a * 0`), which no C# integer does and which a `Decimal` would keep
  (`"" + new Decimal(-0)` is `"-0"`, .NET prints `0`), so TypeScript converts a non-literal
  operand as `new Decimal(x + 0)`. A lifted `decimal?` comparison with an integer operand
  (`d == i`) reads the converted operand once (no second `new Decimal`). Dart prints the
  operand, as for every `decimal` endpoint, and keeps its compound operators (byte-identical).
  Traced by the "Implicit integer-to-decimal conversions" row of the feature-support matrix.

  Delivery (wave 2, group G3, three sequential hand-ins on one branch, each reviewed before the
  next): G3a is FR-080 alone (after G1's `char` clause lands); G3b is FR-078 (declarations and
  names, #600, checked conversions, generic type parameters and the branded rename); G3c is
  FR-077 and FR-079 (the use sites). The 3.0 migration notes in the CHANGELOG list the renames
  (`__negate` / `__plus`, the conversion statics, the companion `op_*` functions) when they
  land. Whichever of G3 and #681 (G4) lands second adds a golden with a static constructor, an
  operator and a conversion.

- **FR-084** (class field layout in generated TypeScript, #682): The TypeScript printer MUST
  print consecutive class fields that each fit on one line (instance or `static`, `readonly`
  or not, with or without a one-line initializer) one after another, with no blank line
  between them. A field that prints on more than one line MUST get one blank line above and
  below it inside the field block; this covers a multi-line initializer (an object literal or
  array literal printed in block form, an arrow function with a block body) and, by decision,
  a field with a doc comment, which prints on its own line above the declaration. A decorator
  printed on its own line counts the same way (the TypeScript AST has no field decorators
  today). The printer measures the rendered field, so the rule follows the actual layout. One
  blank line still separates the field block from the constructor, the accessors and the
  methods, and the members of those groups stay separated by one blank line. The change is
  whitespace only: no token of the generated code changes. Dart output is unchanged (its
  layout does not change before v3).

- **FR-085** (per-target MSBuild packages, #328, ADR-0029): Starting with 3.0.0, the MSBuild
  integration MUST ship as one NuGet package per language target, named
  `Metano.Build.<Target>`. The only one in 3.0.0 is `Metano.Build.TypeScript`; it replaces
  `Metano.Build`, which gets no 3.x version (no stub, no metapackage) and is only deprecated on
  nuget.org after the release. A `Metano.Build.Dart` package is out of scope until the Dart tool
  ships (#28).
  1. *Contents.* Each package carries, in both `build/` and `buildTransitive/`, its own
     `Metano.Build.<Target>.targets` (the target's setting resolution, its CLI flags, its tool
     command, its language tag, its `METANOBUILD001` text and its transpile target with the
     incremental check) and one copy of the shared `Metano.Build.Common.targets`. The shared
     file has a single source in the repository (`src/Metano.Build.Targets/`) and is linked into
     every package; an import guard makes a project that references two build packages load
     it once. Different package releases can carry different shared-file contents: each
     per-target file registers its common-file path and content hash as a
     `_MetanoBuildCommonCopy` item. The build MUST warn when a registered copy differs from the
      loaded shared file and ask the consumer to align package versions (#706). This warning
      uses `METANOBUILD003` and MUST be suppressible with MSBuild `NoWarn=METANOBUILD003`.
     A package predating that registration contract is not a known copy: when the newer
     common file loads first it cannot compare against the older copy; when the older
     guarded file loads first it has no check. The build MUST NOT emit a speculative skew
     warning. This mixed-version combination (including legacy `Metano.Build` 2.x, which
     has no common-file registration) is undetectable and MUST be documented as such.
  2. *Where each part runs.* The shared file cannot build a prefixed property name (`MSB4184`)
     and never reads one. Each per-target resolve target registers one `_MetanoEnabledTarget`
     item per enabled target, with the metadata `Include` (the language tag), `OutputDir`,
     `OutputDirFull`, `ToolchainKeyFile`, `CompilerProject`, `ToolStoreName`, `ArgumentsHash` and
     `PostBuildCommand`, and appends itself to `_MetanoResolveTargetsDependsOn` and its transpile
     target to `_MetanoTranspileTargets`; the shared file batches over those items. In the
     project's own instance, on every build: user-input collection, setting resolution,
     `METANOBUILD002`, the asset projection and effective argument line, the toolchain keys,
     each target's incremental check, and the post-build commands. The argument hash in the
     key MUST track the exact line sent to the worker, including settings changed through `-p:`
     or `Directory.Build.*`; unchanged effective arguments MUST preserve the key timestamp.
     Free-form `FilePrefix` and `ExtraArgs` values MUST be MSBuild-escaped before item
     composition so literal semicolons survive (#706). In a worker instance (the `MSBuild` task on
     the same project with the resolved, escaped values as global properties), only for an
     out-of-date target: the tool probe (`METANOBUILD001`) and the CLI `Exec`. The worker is a
     fresh evaluation: it sees neither items the parent produced by running targets
     (`@(ReferencePath)`) nor items or properties a user target added, so it reads no item and
     no setting of its own. The FR-087.2 synthetic target uses exactly this contract.
  3. *No compiler inside.* A package MUST NOT bundle or install a compiler: a .NET tool cannot be
     a NuGet dependency. Each target's tool (`metano-typescript` for TypeScript) is installed
     separately, preferably through a committed `.config/dotnet-tools.json` and
     `dotnet tool restore`. When the target is out of date and the tool cannot start, the build
     fails with `METANOBUILD001`, whose text names that target's tool and its install commands
     (`MetanoSkipToolCheck=true` and dev mode skip the probe, as today).
  4. *Versioning.* Every build package version is lock-step with the repository version, so
     `Metano.Build.TypeScript` starts at 3.0.0.
  5. *Migration.* A 2.x project migrates by replacing the `Metano.Build` `PackageReference` with
     `Metano.Build.TypeScript`; every `Metano*` property keeps its name and meaning, and a project
     that imports the targets by path imports `Metano.Build.TypeScript.targets` instead. For an
     unchanged single-target project, the same compiler invoked through
     `Metano.Build.TypeScript` and through the 2.x targets MUST receive the same command line and
     produce byte-identical output (in this repository: `targets/` regenerates unchanged).

- **FR-086** (several targets per project, #328, ADR-0029): A project MAY reference several build
  packages, and each one MUST resolve every setting it reads by one rule:
  `<Target>(S) = $(Metano<Prefix><S>) ?? $(Metano<S>) ?? <target default>`, where an empty value
  counts as unset. The TypeScript prefix is `Ts` (`MetanoTsOutputDir`, `MetanoTsImportAlias`,
  ...); a future Dart package uses `Dart`. The unprefixed name is the global setting shared by
  every enabled target, so a single-target project keeps the unprefixed names unchanged.
  1. *Settings list (the contract).* `Metano.Build.TypeScript` resolves exactly `OutputDir`,
     `Enabled`, `Clean`, `ShowTimings`, `Dist`, `PackageRoot`, `SrcRoot`, `ImportAlias`,
     `SourceCondition`, `SkipPackageJson`, `NamespaceBarrels`, `StripInterfacePrefix`,
     `FilePrefix`, `ExtraArgs`, `PostBuildCommand`, `SkipToolCheck` and `CompilerProject`, each
     through the rule above; a test asserts that this list and the resolution lines in the
     `.targets` file match. TypeScript-only settings follow the same rule (no other target reads
     them). A setting cannot be cleared per target: an empty `MetanoTs<S>` falls back to the
     global value. A global `MetanoCompilerProject`, `MetanoExtraArgs` or
     `MetanoPostBuildCommand` reaches every enabled target, so a project with more than one
     target should set the prefixed form (documentation only, no warning).
  2. *Enablement.* A target is enabled when its resolved `OutputDir` is non-empty and its
     resolved `Enabled` is `true` (its default); referencing a build package without configuring it stays
     inert. Settings are resolved when the build runs, not at evaluation, so values set in
     `Directory.Build.targets` (imported after NuGet package targets) are honoured.
  3. *Isolation.* Each enabled target runs in its own MSBuild target (`MetanoTranspileTs`, ...)
     with its own incremental check (`<OutputDir>/.metano-stamp`; its inputs include the references' implementation assemblies, whose bodies the compiler reads, #818, and a list of the input files that changes when a source or
     `MetanoAsset` is added, removed or renamed, #797), its own toolchain key file in
     `obj/`, its own asset selection (`<MetanoAsset Language="...">`, unchanged) and its own
     post-build command; one target's settings never reach another's command line. As in 2.x, a
     post-build command runs on every build, also when its target was up to date; unlike 2.x, a
     disabled target runs none (2.x ran it even with `MetanoEnabled=false`). The public
     target names `MetanoTranspile` and `MetanoPostBuild` remain as aggregates: a user hook
     declared with `BeforeTargets="MetanoTranspile"` runs before any setting is resolved or asset
     projected, so a `Metano*` property it sets or a `MetanoAsset` it adds reaches the tool, and
     one declared with `AfterTargets="MetanoTranspile"` runs after every enabled target, as in
     2.x (`MetanoTranspile` has no dependencies; its body prepares and then runs the per-target
     transpile targets). Each per-target target (`MetanoTranspileTs`) prepares in its body as
     well, so it can be run on its own.
  4. *Collision.* Two enabled targets whose resolved output directories are the same full path
     (each resolved against the project directory and normalized, trailing separators trimmed,
     compared case-insensitively) MUST fail the build with `METANOBUILD002` (an error naming both targets and the directory),
     before any tool runs; a global `MetanoOutputDir` therefore serves a single target only.
  5. *Up-to-date builds.* An up-to-date target MUST be skipped without starting its tool or
     spawning its worker, as today.

- **FR-087** (build-package verification and release gate, #328): The repository MUST verify the
  packed build package the way a NuGet consumer uses it.
  1. *Pack smoke test.* A script packs `Metano`, `Metano.Compiler.TypeScript` and
     `Metano.Build.TypeScript` at a throw-away version into a local feed, installs the tool from
     that feed into a local tool manifest, and builds a throw-away consumer that references
     `Metano.Build.TypeScript` with `PackageReference`; it asserts the generated `.ts` file,
     `.metano-stamp` and `package.json`, and that a second build skips the transpile; a two-project scenario also asserts that a body change (an `[Inline]` value) or a `[Name]`
     pin in a referenced project, and a removed source, re-transpile, and that an unchanged build
     does not (#818, #797). A negative
     case without the tool asserts `METANOBUILD001` and its install text. It uses an isolated
     NuGet package cache and `DOTNET_CLI_HOME`, so it neither reads nor pollutes the developer's.
     For #706, nuget.org downloads MUST use a reusable HTTP cache and bounded retries for
     transient restore failures, without retrying compilation or assertion failures. The
     smoke test MUST also build a consumer that receives the targets only through a NuGet
     dependency's `buildTransitive` assets, and a project-reference graph with
     `-graphBuild -isolate`. Both cases MUST transpile successfully; the isolated graph's
     second build MUST skip transpilation. Changed effective arguments through `-p:` and
     `Directory.Build.*` MUST rerun the tool and preserve literal semicolons in generated
      file prefixes. Consumers MUST exercise default package-root inference without a
      `MetanoPackageRoot` pin, including macOS's `/var` → `/private/var` alias (#706).
       Inference (baseline FR-030) MUST walk the written output path's ancestors for markers,
       package files and the fallback parent; the chosen root MUST stay on that path.
       Only containment comparisons against the project directory resolve existing symlinks,
       including ancestors of not-yet-created output paths; aliases of one workspace MUST NOT
       cause `MS0044`. Outputs linked to sibling or far trees MUST retain their written-path
       package ownership and writer-relative paths. The boundary ranking and refusal for
       genuinely unrelated written outputs remain unchanged. An explicit package root
      still wins as given. Deprecating `Metano.Build` on nuget.org remains outside #706.
  2. *Multi-target tests.* With a test-only `.targets` that declares a synthetic second prefix
     over the shared file, tests cover precedence (prefixed over global over default),
     independent stamps, `METANOBUILD002`, the inert unconfigured package and the up-to-date skip.
  3. *Where it runs.* The smoke test runs in CI on every pull request and, subject to the
     maintainer's approval of the workflow change, in the release workflow before
     `semantic-release` (as the `metano-runtime` smoke test does), so a broken package fails the
     release before a tag exists.
  4. *Name sweep.* Outside `CHANGELOG.md`, the shipped-feature specs and the deprecation and
     migration notes, no live document or project still names the `Metano.Build` package or the
     `Metano.Build.targets` file.

- **FR-093** (record `ToString` text, #664): A record (class or struct) emitted as a class MUST
  carry a `toString()` that returns the text .NET's synthesized `ToString` returns.
  1. *Format.* `Name { P1 = v1, P2 = v2 }`, and `Name { }` when no member prints. `Name` is the
     C# simple name of the record: no generic arity, no containing type, no `[Name]` rename
     (`RsGeneric<int>` prints `RsGeneric { Value = 1 }`, a record nested in `Outer` prints
     `Inner { X = 1 }`). Each `P` is the C# member name, also when `[Name]` renames the member.
  2. *When.* The fate comes from Roslyn, not from a Metano rule: `toString()` is synthesized
     exactly when the record's symbol has an implicitly declared parameterless `ToString`. A
     user `ToString()` (emitted as the user's method, as today), a `sealed override ToString()`
     in a record base (the derived record has no implicit member and inherits the base's) and
     every other declared form are left untouched; an `abstract override ToString()` in a base,
     and a non-sealed user `ToString()` in a base, make the derived record synthesize its own,
     as .NET does.
  3. *Printable members.* Exactly C#'s rule, at the record's own level: its public instance
     fields and its public instance properties that have a getter (a `private get` included),
     in declaration order (the positional properties first). Static, `const`, protected and
     private members, indexers, `EqualityContract` and `override` properties are not printed;
     an `override` property is printed once, by the record that declares the original, which
     reads it through virtual dispatch. A property is read through its accessor (`this.p`), so a
     computed property runs; this differs from equality, which reads storage (FR-060, #384).
     A member marked `[Ignore]` does not exist on the emitted object and is skipped (W7-4).
  4. *Member text.* Each value prints exactly what the interpolation hole `$"{this.P}"`
     prints today (baseline FR-014, #308, #694; one shared default-text lowering in the core):
     `True` / `False`, `float` and `double` in .NET's shortest round-trip form, a `decimal`
     with its scale, an enum by member name, a `null` (reference or nullable value) as the
     empty string, a `char` as its character, and any other object (a nested record, a runtime
     collection) through its own `toString()`. A divergence of that lowering is a default-text
     gap, not a record gap: records inherit it (a C# array prints its elements, a plain class
     prints `[object Object]`, a `DateTime` prints its Temporal text) and those gaps are fixed
     separately.
  5. *Not covered.* A user `PrintMembers(StringBuilder)` in the record or in a record base
     (W7-4, deferred), and a record that declares another member emitted as `toString` (a
     `ToString(string)` overload, which would need the synthesized `ToString` to join the
     overload group as spec 013 FR-038 does for `Equals`), report `MS0001` (a warning) on the
     record and emit no synthesized text; the record keeps today's output. Dart is deferred
     (W7-4): its output stays byte-identical.

- **FR-094** (record `ToString` protocol and edges, #664): The TypeScript target MUST emit the
  text of FR-093 through one protocol that chains like C#'s `PrintMembers`.
  1. *Protocol.* A record that a record can derive from (not sealed, or with a record base)
     declares `protected $printMembers(): string`, which returns its own members' text
     (`P1 = v1, P2 = v2`); a derived record's override starts with the base's text, base first
     and separated by `", "` only when both are non-empty (C#'s `if (base.PrintMembers(b))
     b.Append(", ")`). `toString()` returns `` `Name { ${this.$printMembers()} }` `` when the
     record's own list is non-empty, and the runtime-conditional
     `` m ? `Name { ${m} }` : "Name { }" `` otherwise. A sealed record or a record struct with
     no record base inlines its members into `toString()` and declares no `$printMembers`:
     nothing can chain to it. The `$` prefix cannot collide with a C# identifier. Both members
     carry `override` when the class extends an emitted base (`noImplicitOverride`).
  2. *Cross-package base.* A derived record reads its base's text as
     `super.$printMembers?.() ?? ""`, so a base package generated before this requirement (no
     `$printMembers`) prints only the derived members at run time instead of throwing
     (W7-4). The tolerance is run time only: a library whose records derive from a record of
     another package MUST regenerate that base package, because an old base and a new derived
     record (or the reverse) do not type-check together (TS4113, TS2339, TS4114); the release
     note says so.
  3. *Cycles.* There is no cycle detection (it would cost every record a per-call check and an
     import). A record that reaches itself through its members overflows the stack with a
     JavaScript `RangeError` (.NET's `PrintMembers` throws
     `InsufficientExecutionStackException`); `toException` keeps it catchable as an
     `Exception`. Documented divergence (W7-4: no mapping).
  4. *`[PlainObject]` at a text site.* A `[PlainObject]` type has no class, so it has no
     `toString()` and prints `[object Object]`. A value of such a type written at a default-text
     site (an interpolation hole, a string concatenation operand, or a `ToString()` call that
     binds to `object`'s or the record's synthesized `ToString`) MUST report `MS0001` (a
     warning; loud over silent, W7-4). The TypeScript target decides and words it, through
     the core's `UnprintableTextReason` port; Dart sets no answer and reports nothing. A `[PlainObject]` member of a record prints
     `[object Object]` inside the record's text without a diagnostic: no source site asks for it.
  5. *Cost (spec 011 NFR-008).* Class methods are not tree-shaken, so every used record keeps
     its `toString` and the formatters its member types need (`formatDouble` / `formatSingle`
     for the first floating-point member, `formatBoolean`, `enumText`). No opt-out (W7-4).
     `size-budgets.json` records the measured rows `record toString (int/string members)` and
     `record toString (double member)`; an unused record stays 0 bytes (golden side-effect
     check).
  6. *Unchanged.* JSON and runtime descriptors are not touched (the display name is a literal).
     A record used as a key that the runtime turns into text (a `ConcurrentDictionary` key's
     `toJSON`) now prints the record text instead of `[object Object]`.

- **FR-095** (switch expressions without closures, #735, decision H-1): The TypeScript target
  MUST lower a C# `switch` expression without allocating a closure wherever an enclosing
  statement list exists, keeping .NET's evaluation order: the subject is evaluated exactly once,
  the arms are tested in source order with their `when` guards, and only the matched arm's
  result is evaluated.
  1. *Shapes.* A switch whose subject is side-effect free, whose arms bind nothing and whose
     last arm is an unguarded `_` stays a nested conditional chain over the subject (unchanged).
     `return value switch { … };` stays a block of `if`s that return or throw (wave 7, #735).
     Every other switch expression — a local initializer, an assignment, an argument, an
     operand, an arm result of another switch — lowers to a conditional chain over a temporary:
     `($s = subject, test1 ? result1 : test2 ? result2 : tail)`.
  2. *Temporaries.* `$s` is an untyped `let` declared at the head of the innermost statement
     list (the temporaries mechanism of spec 011, contracts/runtime-collections.md § Indexer
     compound assignments). A `switch` statement's sections are not statement lists of their
     own: every `case` clause of a JS `switch` shares one scope, so the temporaries of a
     section go to the head of the list that encloses the `switch` statement, and sections
     are not wrapped in blocks. A user local declared in one section and used in another is declared ahead of the switch statement (an uninitialized typed `let`, or the `const` with its initializer), in a block that encloses the declaration and the `switch`, and its declaration in the section becomes an assignment, so a later section that runs alone never meets it in the temporal dead zone (#755). This is the one rule for user locals shared by sections, for a `switch` on constants and a `switch (true)` alike.
     Each arm designator the arm reads becomes its own typed `let` named after it with a `$`
     prefix (`Circle c` → `let $c: Circle;`), assigned by the arm's test right after the check
     that narrows the subject (`$s instanceof Circle && ($c = $s, guard)`), and every read or
     write of the designator in the arm's guard and result names the temporary. A designator
     the arm never reads takes no temporary. Nested switches take distinct names (`$s1`,
     `$c1`).
  3. *Throws.* An unguarded catch-all arm (`_`, `var x`) is the tail; later arms are
     unreachable. Without one, the tail throws through the runtime helper
     `throwError(error): never` with the `SwitchExpressionException` the statement form throws
     (`throwError(new SwitchExpressionException($s))`). Every C# throw expression, in a switch
     arm or anywhere else (`x ?? throw new X()`, `flag ? 1 : throw new X()`), lowers to
     `throwError(<exception>)` instead of a throwing arrow.
  4. *Fallback.* The self-invoking arrow, whose bindings are fresh per evaluation, stays in two
     cases: (a) no statement list encloses the switch (a field or static initializer, a
     constructor-initializer argument); (b) a lambda in an arm's guard or result reads one of
     the arm's designators. One temporary per statement list would be shared by every closure
     created from a loop header (`while`/`do` condition, `for` increment), which evaluates the
     switch many times per execution of the list, and such an arm allocates a closure anyway.
  5. *Scope.* Dart output does not change.
- **FR-096** (`[ObjectArgs]` on primary constructors and positional records, #739, decision W8-1,
  amends the `[ObjectArgs]` constructor behaviour): `[ObjectArgs]` on a constructor already emits
  a positional runtime constructor plus `static create(args: {...})` and lowers `new T(...)` to
  `T.create({...})`. This requirement extends the same lowering, with no new emission strategy,
  to the constructors that have no declaration to put the attribute on: (a) a primary
  constructor (a positional record or a C# 12 class), marked with `[method: ObjectArgs]` on the
  type declaration; (b) a type-level `[ObjectArgs]` on a type that declares exactly one
  constructor (the primary one, or a sole explicit one), which applies to that constructor. A
  defaulted parameter becomes an optional property whose C# default the factory applies, so a
  call site omits it (`new Point(1)` lowers to `Point.create({ x: 1 })`); a parameter is keyed
  by its camel-cased name (a positional record's `X` is keyed `x`), at the declaration and at
  every call site, positional or named, in the type's own package and in a referencing one. The
  runtime constructor stays positional, so record equality, `hashCode`, `with`, `toString`,
  deconstruction, `super(...)` of a derived record, `[JsonSerializable]` factories and the
  MS0061 construction paths are unchanged. An `[ObjectArgs]` the lowering cannot honour MUST NOT
  be ignored: a type-level attribute on a type with several constructors (or none) reports
  `MS0001` (the existing "not supported" code) and no `create` is emitted, and a constructor
  `[ObjectArgs]` combined with constructor overloading keeps reporting `MS0001`. The Dart target
  is unchanged: it never lowers a construction to `create` (no object-literal expression), so a
  Dart construction stays positional. The call site builds its object from the arguments Roslyn
  bound, in the order the source evaluates them (`new P(Y: F(), X: G())` lists `y` then `x`,
  with no temporary), folds a `params` expansion into one array literal and omits an argument
  equal to its default. A `params T[]` parameter is the optional property `items?: T[]`
  defaulting to `[]`, and the factory spreads it into the positional constructor
  (`new T(name, ...items)`). Limitations, each reported as `MS0001` with no factory emitted and
  constructions kept positional: an abstract type; a `[PlainObject]` type (it already
  constructs as an object literal, so `[PlainObject]` wins); a type declaring a static member
  named `create`; and a derived type whose factory could not override its nearest base factory
  without breaking TypeScript's static-side rule (TS2417): the derived factory is emitted as
  `static override create` only when the base is not generic and every base key appears in the
  derived arguments with the same type and requiredness. A type-level attribute on a type with
  no constructor (a static class) reports that there is no constructor to lower.

- **FR-097** (C# 14 extension operators, #761, decisions W9-3 and W9-8, extends FR-077 / FR-078
  to the extension-member model of #156): An operator declared in a C# 14 `extension` block
  (`static class SigOps { extension(Sig) { public static Sig operator +(Sig a, Sig b) … } }`)
  MUST run at every application C# binds to it, never as the native JS operator. The TypeScript
  target emits it as an exported function of the extension class's module (the file of
  `SigOps`, beside its extension methods and properties), named `__` + FR-078's operator name +
  `$` + the block receiver's emitted type name without type arguments (`__add$Sig`,
  `__negate$Sig`, `__checkedMultiply$Sig`; `extension<T>(Box<T>)` → `__add$Box<T>(a: Box<T>, b:
  Box<T>): Box<T>`, a primitive receiver its TypeScript keyword, `__add$number`). A `[Name]` on
  the operator replaces the operator name (`[Name("plus")]` → `__plus$Sig`). The function takes
  the operator's parameters and the block's and the operator's type parameters. Each application
  is FR-077's `IrUserOperatorApplication`, whose `BoundOperator` names the extension class as
  its declaring type and carries the block's receiver type (`ExtensionReceiver`), so binary,
  unary, relational, equality (lifted included), `++` / `--`, compound assignment and `checked`
  applications keep FR-077's evaluation rules and call the module function (`__add$Sig(a, b)`,
  `c = __increment$Sig(c)`). The consumer imports it from the class's module, or from its package
  for a referenced one. Each function name is registered in the project-wide extension-helper
  registry, so two classes exporting the same name (the same operator for the same receiver)
  report `MS0021` (error) at the declaration, and no barrel exports a name twice. Not lowered,
  reporting `MS0001` (warning) at each application with the throwing placeholder, and at the
  declaration where one is emitted: an instance (compound-assignment or increment) extension
  operator; a `true` / `false` extension operator; two or more extension operators of one class
  with the same metadata name and the same receiver type (same or another block), unless each one
  carries its own `[Name]`; an `[Ignore]`d extension operator (no function, `MS0001` only at the
  applications); and an extension operator whose class is not emitted (an `[Ignore]`d class, or a
  class of a library that is not transpiled). Dart output is unchanged: Dart keeps user operators
  as the source wrote them.
- **FR-098** (workspace roots and one package version, #764, #765, decisions W9-6, W9-7):
  *Workspace roots.* Package-root inference (baseline FR-030) MUST treat a `package.json` that
  declares `workspaces`, or that sits beside a `pnpm-workspace.yaml`, as a workspace boundary and
  never adopt it: the walk stops below it, and the documented fallback (the parent of the output
  directory) applies. A fallback that is itself a workspace root MUST be refused with `MS0044`
  (the message names the workspace root), so a workspace root's `package.json` is never written
  and `MS0007` is not raised against it, unless its `name` equals the `[EmitPackage]` name (a
  single-repo library that declares `workspaces` for its examples), in which case it is adopted. An explicit package root still wins as given.
  *One version.* The generated `package.json` MUST always carry `version`, from the same source as
  the range a cross-package consumer writes for the package: `[EmitPackage(Version)]` when set
  (an exact version, or the base version of a `^`, `~`, `>=` or `=` range; `<`, `>`, compound
  ranges and protocols such as `workspace:*` name none and fall back, with a warning unless it is a
  protocol), else the assembly's informational version without build metadata, else
  `Major.Minor.Build`. The range keeps its style (the explicit value verbatim; `^` plus the
  version when derived; `workspace:*` for an unversioned assembly), so the package's `version`
  satisfies every range written for it. Metano owns the field and replaces a hand-written value
  (reported as an informational message); MSBuild `Version` is the intended source; no
  `workspace:` protocol is introduced. Without an import alias, the bare `#` and `#/*`
  `imports` entries an earlier version seeded are removed while they still hold exactly the
  writer's own mapping and no condition of the consumer's; a retargeted, extended or
  host-authored entry stays (this amends spec 004 FR-014). Traced by
  `PackageWorkspaceTests` and `EmitPackageTests`.
- **FR-102** (internal types reached from emitted code, #770, decision W9-9, amends the
  `[assembly: TranspileAssembly]` selection rule): under `[assembly: TranspileAssembly]`, a
  non-public type of the assembly that emitted code reaches MUST be transpiled, so the output
  never names a symbol it does not declare. A type is reached when the signature or body of a
  transpiled type references it (base list, members, initializers, parameter defaults, bodies,
  extension-method calls), directly or through another reached type; a reached nested type
  brings its outermost container, which carries it. A reached type is emitted as its own
  module and imported where used, but it stays off the package's public surface: no namespace
  barrel re-exports it, so `package.json#exports` (built from the barrels) does not expose it,
  and a referencing package never imports it. `[Ignore]` keeps winning (a reference stays
  `MS0013`), and `[External]` / `[JsCallable]` types stay ambient. A reference from emitted code
  to a type of the assembly that is still not emitted (a `file`-local type) MUST report
  `MS0002` (warning) at the reference site instead of producing a silent `ReferenceError`. So MUST a reference from a referencing package to a non-public type of a
  Metano package (reached through `[InternalsVisibleTo]`), which it cannot import. The
  explicit `[Transpile]` mode is unchanged: an internal type with `[Transpile]` keeps its barrel
  entry, and only `[TranspileAssembly]` assemblies reach types automatically.
- **FR-106** (Metano warnings honour the project's warning settings, #785, **behaviour change**):
  The build target MUST pass the project's `TreatWarningsAsErrors`, `WarningsAsErrors`,
  `WarningsNotAsErrors` and `NoWarn` (and MSBuild's `MSBuildTreatWarningsAsErrors`,
  `MSBuildWarningsAsErrors`, `MSBuildWarningsNotAsErrors`, `MSBuildWarningsAsMessages`) to the
  compiler CLI as `--warn-as-error`, `--warnings-as-errors`, `--warnings-not-as-errors` and
  `--no-warn` (comma-separated lists; entries other than a Metano code, `MS` and four digits,
  are ignored). The CLI applies them to every Metano diagnostic of the run: a warning in
  `NoWarn` is dropped; otherwise a warning becomes an error when it is not in
  `WarningsNotAsErrors` and either `TreatWarningsAsErrors` is set or it is in
  `WarningsAsErrors` (`NoWarn` wins over escalation, `WarningsNotAsErrors` over both
  escalating settings). A run that reports an error, escalated warnings included, MUST write
  neither the `.metano-stamp` nor the incremental cache, and MUST remove an existing stamp, so
  the next build runs the transpile and fails again. The incremental cache (format 6) MUST
  store the non-error diagnostics of the target and emit stage, and a run served from it MUST
  report them again, together with the frontend's, through the same policy; a group of the
  per-group cache that reported a diagnostic MUST NOT be reused, so its diagnostic is
  reported again. The CLI keeps the warnings of a successful run in
  `<output>/.metano-diagnostics` (one canonical line each) and the build target prints it
  when it skips the transpile as up to date, so a warning is reported by every build. A
  project that sets `TreatWarningsAsErrors` and already reports Metano warnings starts
  failing; the warning is fixed, or its code is listed in `WarningsNotAsErrors` or `NoWarn`.
  Changing a warning setting changes the CLI arguments, which reruns the transpile.
  Codes in `MSBuildWarningsAsMessages` are suppressed (as `NoWarn`), not demoted to messages.
  Only Metano codes are passed, so editing a compiler-only `NoWarn` does not rerun the
  transpile. Warnings of the package.json stage are appended to `.metano-diagnostics` and a
  failed load removes the stamp. `.metano-*` files are build state: they are not committed and
  not published.

- **FR-107** (TypeScript 7 and the Temporal polyfill types, #786): `metano-runtime`'s
  declarations of the Temporal-typed API reference `@js-temporal/polyfill`, and so do the
  generated files that use a mapped BCL date/time type. The polyfill 0.5.1 declarations clash
  with the built-in `Temporal` and `Intl` types of TypeScript 7 under `lib: esnext`
  (TS2430), which `skipLibCheck: true` or a `lib` without `esnext.temporal` (for example
  `es2024`) avoids. This is a documented limitation of the pair of versions, not of the
  runtime; removing it needs the polyfill, or a design that changes the Temporal-typed
  public API (see the issue).
- **FR-108** (instance field shape, #795, #787 N4/N11, #801, decisions W10-1 / W10-1a / W10-1b /
  W10-10): An
  instance field of an emitted TypeScript class, including an auto-property's storage, an
  event's backing field and a promoted record or primary-constructor parameter, MUST be a
  type-only declaration (`declare readonly x: number;`), and every constructor path MUST run
  one synthesized store per such field (its head store), right after `super(...)` and the
  runtime type-argument slot, in
  the constructor that runs the C# initializers (not one chaining with `: this(...)`): first
  the promoted members in parameter order, then every other field in declaration order with its
  C# initializer, its default, its captured parameter, or `undefined!` when it has none. The
  own keys are therefore created in declaration order on every path. The head store is the
  only synthesized one, not the only write: an initializer that reads a captured
  primary-constructor parameter runs after the captured-field stores, so its field's head store
  is the default and the initializer stores it again, and a field the constructor body assigns
  outside the trailing run below keeps its head store before the body's. A field without an
  initializer whose type is a struct of the compilation that declares a constructor stores the
  struct's zero value (`Pt.$default()`, #802); any other value type with no TypeScript default
  (a date or time, a tuple, a type parameter, a generic or `[InlineWrapper]` struct, a struct of
  another package) keeps `undefined!` and reports MS0001 as a warning at the member unless every
  declared constructor assigns it. A trailing run of fields
  without an initializer (or with a literal one) that every constructor assigns first, in
  declaration order, before any code can observe the instance (no `this` or `base` in the
  stored values) stores only that body assignment, in a class without a base class (a base
  constructor runs first and may publish `this`), including a member an explicit constructor
  promotes when every field after it qualifies too. Left as class fields: static, abstract and
  `override` fields (`declare override` is TS1243), `#private` fields, and the type-info caches
  of a JSON serializer context (`private _account?: TypeSpec<Account>;`). A static class MUST
  emit `private constructor() { }`; a class that declares no constructor and has nothing to run
  in one emits none. An imported binding that a constructor parameter, promoted parameter or local
  (an `out var` designation included) shadows is read under an import alias inside the
  constructor and its initializers
  (`import { compute, compute as compute$1 }`), whether it is a function or a type (a static
  receiver, a `new` callee, an `instanceof` operand, an enum member receiver); the alias of a
  name is the same in every class of a file; parameters are never renamed (their names
  are API). One that the module itself declares cannot be aliased and reports MS0001. A parameter of an explicit
  constructor is promoted only to an auto-property its body initializes from it
  (`Name = name;`, #801) in its leading run of plain stores to the instance whose values never
  read it (after a `return`, a read of the member or a store through another receiver, the
  property keeps its default store); a computed property keeps its getter. Consumers compiling the output with
  Babel's `preset-typescript` need `allowDeclareFields`. Tests:
  `tests/Metano.Tests/InstanceFieldStoreTests.cs`, conformance `FieldStoreProbes` and
  `ConstructorShadowProbes`.
- **FR-109** (strict-mode output without avoidable noise, #787, #794, #796, #798, #791, #802,
  decisions W10-2 / W10-2a / W10-3 / W10-3a / W10-3b / W10-4 / W10-4a / W10-11): The TypeScript
  output MUST type-check under `--strict` and MUST NOT carry guards, annotations or assertions
  that neither .NET semantics nor the type checker need, without ever dropping a guard on
  Roslyn's flow state alone:
  1. *Emission noise (#787).* A nullable array element read is bare (`a[i] as T | null`); an
     empty catch whose variable is unused takes the optional binding (`catch {`); a catch
     variable binds straight from the caught value (`const error = toException(e$)`); a string
     `const` field or local (constant interpolations and `char` concatenation included) folds
     to its value; `else if` chains print flat and `is not null` prints `x != null`.
  2. *Concatenation guard (#787 N2).* A string compound append keeps the `?? ""` guard on its
     target unless the target is a `var` local whose initializer and every assignment are
     string expressions that cannot yield `null`: a literal, an interpolation, or the built-in
     concatenation. A user-defined `operator +` (an operator method, even one whose result
     reads as `string`) is not one, and neither is a deconstruction, `out` / `ref` argument or
     `??=` write.
  3. *Null-forgiving literals (#794).* `null!` / `default!` converted to a non-nullable
     reference type keeps the assertion (`null!`), decided from the destination's declared
     type (a field, property, local or parameter symbol, a parameter, a return type), never
     from its flow state: a store to a non-nullable member inside a constructor or after a null
     test keeps it. An explicitly typed local initialized so keeps its annotation. A local is
     judged by its emitted declaration: a store to a `var` local keeps `null!` unless that
     declaration is annotated `T | null` (rule 4), so a deconstructed or pattern-declared local
     always keeps it.
  4. *Local annotations and narrowing (#796, #798).* A `var` reference local is annotated
     `T | null` only when a later assignment has a maybe-null value (a suppressed `null!` is
     not one). A carried `!` is left out
     where TypeScript narrows by itself (the right operand of `&&`, the true branch of an `if`
     or conditional guarded by a null test of the same local with no assignment between), and
     kept on a captured, reassigned `let` read inside a lambda or local function.
  5. *Strict-mode names (#791).* Identifier positions (locals, parameters, labels, top-level
     functions and classes) escape the strict-mode words (`implements`, `interface`, `let`,
     `package`, `private`, `protected`, `public`, `static`, `yield`, `await`, `eval`,
     `arguments`) with a trailing `_`; member names keep them, and a promoted parameter so
     named is declared as a field stored from the escaped parameter. A top-level type keeps
     its escaped name in every position that names it: its declaration, its static overload
     dispatcher's receiver, the names derived from it (constructor selectors, factories), and
     a derived class or caller in another package. A `[Name]` override is used as written.
     Dart rejects its reserved words in member positions too, so the Dart target escapes them
     in identifier and member positions alike, at the declaration and every use; every member
     is escaped (fields, properties and methods, promoted or not), not only promoted record
     members, because Dart rejects the bare word in any of them (supervisor decision, wave 10).
  6. *Struct defaults (#802).* `default` / `default(S)` of a struct that declares a
     constructor is the struct's zero value, `S.$default()`, in every position, parameter
     defaults included, on a target that declares one; Dart keeps the placeholder and its
     `MS0001` warning.
  Tests: `tests/Metano.Tests/EmissionNoiseTests.cs`, `NullForgivingLiteralTests.cs`,
  `NullTestNarrowingTests.cs`, `StrictModeReservedWordTests.cs`, `StructDefaultTests.cs`;
  conformance `EmissionNoiseProbes`, `StrictWordClassProbes`, `StructDefaultProbes`.
- **FR-099** (placeholder severity, wave 9 decisions W9-13 / W9-13a): An unsupported construct
  whose emission replaces executable code with a placeholder — a `/* TODO … */` marker, a dropped
  statement or expression, a read that yields nothing instead of the value — MUST report `MS0001`
  with the severity of the target: an **error** on TypeScript (the output is known to be wrong),
  a **warning** on the Dart prototype, which does not render every body yet (revisit when it
  leaves prototype status). `MS0001` stays a **warning** on every target where the emitted code
  still preserves the .NET behaviour or fails loudly at run time (the expression placeholder that
  throws when reached). Each report names the C# construct (`The C# for statement is not
  supported by the Dart target.`) and carries the C# source location: the core records each
  statement's span on the IR (`IrStatement.Span`, not part of its equality) and a target that
  renders the IR later reports at the statement it is rendering. The rule lives at the
  placeholder emission points, in one place (`Extraction/DroppedCodeDiagnostics.cs`, with the
  target's `DroppedCodePolicy`): a statement no target models (core, at the statement: `lock`,
  `goto` / `goto case` / `goto default`, …), a `using` declaration (`using var x = r;`, whose
  `Dispose` would never run), a positional pattern element no member answers (core), a node the
  TypeScript bridge has no lowering for (statement and expression fallbacks), and each Dart
  body-printer placeholder (a statement or expression it has no Dart form for, an `[Emit]`
  template, an `is` pattern, a list or positional pattern outside a `switch`, a `not` pattern in
  a `switch`, an unsupported pattern); the Dart static-constructor drop and the Dart overloads
  that keep only the primary signature stay warnings at their declaration. Not placeholders: an
  empty statement (`;`) emits nothing, and so does a local function declaration, which no target
  lowers yet: an unused one (or one only its own body or a `nameof` names) drops nothing that
  runs and reports nothing, while one referenced from elsewhere (matched by symbol: a call, a
  method group, an event handler, a call before the declaration) reports the dropped declaration
  at its name, besides the warning each reference reports (its placeholder throws). Tests: `tests/Metano.Tests/DiagnosticsTests.cs`.

- **FR-100** (labeled statements, #772, decision W9-12): A C# label on a loop (`while`, `do`,
  `for`, `foreach`) or a `switch` statement MUST be kept, and the labeled `break label;` /
  `continue label;` of C# 15 MUST lower to the native label and jumps of the target: in
  TypeScript `outer: for (…) { … continue outer; }` and `dispatch: switch (…) { … break
  dispatch; }`, on the loop itself when a lowering wraps it in a block (the indexed
  `List<T>` / `string` `foreach`) and on the `switch` inside the block that holds a hoisted
  subject; in Dart the same label syntax. A labeled `continue` from a nested loop counts as a
  `continue` of the loop it names, so a lowering that moves a `for` increment into the body does
  not skip it. A label on any other statement can only be a `goto` target; it is emitted without
  the label, and the `goto` (also `goto case` / `goto default`), which has no JavaScript
  equivalent, reports `MS0001` (FR-099). A label keeps its C# spelling, so two labels C# tells
  apart stay apart (`Outer` and `outer`); a JavaScript reserved word is prefixed with `$`
  (`$function:`), which no C# identifier holds. Tests:
  `tests/Metano.Tests/LabeledStatementTests.cs`; conformance `LabeledJumpProbes`.

- **FR-101** (null state proved by a forgiving construct, #775): Where C# flow analysis
  (`NullableFlowState`) proves a variable, parameter, field or property declared with a nullable
  reference type non-null by a construct a static type checker does not follow — a
  null-forgiving `x!` earlier in the flow (`stack!.Value` then `stack.Prev`), or a call whose
  nullable attributes prove it (`ArgumentNullException.ThrowIfNull(x)`, `string.IsNullOrEmpty`
  false, a `[MemberNotNull]` method) — the read MUST be asserted non-null (`stack!.prev`) where
  it is used as non-null: the receiver of a member access, an invocation or an indexer, and an
  argument to a parameter declared non-nullable. The IR carries the assertion as the existing
  `NonNullAssertion` unary; it has no runtime effect in TypeScript. A read whose state comes from
  an assignment, a local's initializer or a null test of the same variable is left unasserted,
  since checkers narrow on those; the origin is the last such event before the read in the
  member's source. Nullable-disabled code has no flow state and is unchanged. The IR records why
  the read is proven (`IrFlowNonNullProof`): the Dart target leaves out the assertion of a local
  or parameter read after `x!`, which Dart promotes itself, and keeps the others; TypeScript
  keeps every one (erased, type-only). Known Dart divergence: a field or property keeps its `!`
  (Dart never promotes a non-final field), and since C# does not reset a field's state across a
  call that may change it, Dart's run-time `!` throws where .NET lets such a `null` through (a
  `null` stored in a slot C# proves non-null). Tests:
  `tests/Metano.Tests/ForgivenNullStateTests.cs` (output type-checked with `tsgo --strict`).
- **FR-103** (deconstruction, #779, Group U): Every deconstruction C# accepts in statement
  position MUST lower without a closure and keep .NET's evaluation order: the receivers and
  indices of the existing variables written are evaluated first, left to right, then the right
  side in full — every element of a tuple literal first, then every `Deconstruct` call, depth
  first — then the writes, left to right. Each written receiver and index, a local included
  (the right side or a closure may reassign it), is read once into a `const` before the right
  side; each part converts to its variable as an implicit tuple-element conversion does
  (`(long l, decimal d) = IntPair()`). This covers
  `var (a, b) = e`, `(var a, var b) = e`, `(T a, U b) = e`, a mix of declarations and existing
  variables (`(x, var y) = e`), nesting (`(a, (b, c))`), discards, `foreach (var (k, v) in e)`,
  and the expression body of a `void` member or lambda (`void Swap() => (a, b) = (b, a);`).
  1. *Parts.* A value that lowers to a JS array (a `ValueTuple`, an array, a `KeyValuePair`, a
     `Tuple`, a `[JsTuple]` record) is read by position (`const [a, b] = e`); a record's
     synthesized `Deconstruct` reads its positional members (`const { x, y } = p`); a user
     instance `Deconstruct` of a transpiled type is called and its multi-value result read by
     position (`const [a, b] = p.deconstruct()`, the overload's own name when `Deconstruct` is
     overloaded). A nested part taken apart by a `Deconstruct` call is bound to a temporary and
     deconstructed after the enclosing binding.
  2. *Tuple literals.* A tuple literal on the right side is taken apart element by element
     (`var (n, s) = (1, "x")` → `const n = 1; const s = "x";`), so no array is allocated only
     to be read back; a discarded element is still evaluated for its effects.
  3. *Existing variables.* A local, field, property or array element is written after
     everything else is evaluated, from a `$partN` temporary (a constant is written as is):
     `(x, y) = (y, x)` → `const $part0 = y; const $part1 = x; x = $part0; y = $part1;`.
  4. *Not supported* (MS0001, the unsupported placeholder): an extension or BCL
     `Deconstruct` (`DateOnly`), a write through a collection indexer or an extension member,
     and a deconstruction whose value is used (`var t = (x, y) = …`, a `for` incrementor).
  5. *Dart.* A deconstruction of a tuple (a Dart record) prints as a record pattern
     (`var (a, (b, _)) = e;`), a tuple literal as one declaration per element; every other
     shape keeps the unsupported placeholder.

- **FR-104** (indistinguishable dispatcher branches, #778, Group U): A TypeScript overload
  dispatcher (a constructor, a class method group, or the function group of an
  `[ExportedAsModule]` / `[NoContainer]` class or an extension container) MUST NOT keep a branch
  whose runtime test is the same JavaScript as the test of a branch tried before it
  (`Func<Action, T>` and `Action<Action>` are both a one-parameter function; `JsArray<T>` and
  `T[]`, `int[]` and `double[]` are both an array): the branch and its overload signature are
  dropped, the overload's fast path (a constructor's factory) stays, and `MS0063` is reported at
  the overload, naming the overload that takes its calls and the function hand-written TypeScript
  must call (`Reactivity.rootActionOfAction`). C# call sites already bind to the fast path
  (spec 013 FR-018) and are unchanged. `Function.length` is never an exact test (it leaves out
  ignored, defaulted and rest parameters: `Probes.run(() => 42)` must not throw "No matching
  overload"): at a parameter position where the overloads' delegates have one arity, the test is
  `typeof === "function"` alone, so they share it and the dispatcher keeps the overload whose
  delegate returns a value (`Func<Action, T>` over `Action<Action>`). Where the arities of the
  overloads whose argument counts overlap differ, each takes a non-overlapping `length` range
  (arities {0, 2, 3} test `< 2`, `>= 2 && < 3`, `>= 3`), so the dispatch order does not matter
  and the smallest, with no lower bound, takes a callback that ignores its arguments; overloads
  of disjoint argument counts test no arity. Limits of `Function.length`: a defaulted or rest
  parameter shortens it, so `(a, b = 0) => {}` reaches the one-parameter overload; a same-arity
  value/`void` pair exposes only the value-returning signature to TypeScript, and `MS0063` names
  the other's fast path (a constructor's factory). A pair that differs only by a branded 64-bit
  type keeps its existing once-per-group `MS0063`.
  Dispatchers that also dispatch to a synthesized member and the static groups of a branded
  companion object apply the delegate test but drop nothing.

- **FR-110** (C# 14 extension properties and indexers, the extension-member model of #156,
  #523, decision W10-6): A property or indexer declared in a C# 14 `extension` block MUST run its
  accessors at every use. The TypeScript target emits each accessor as an exported function of
  the extension class's module: a property `P` as `p$get(receiver)` / `p$set(receiver, value)`
  (a static one without the receiver), an indexer as `item$get(receiver, ...indices)` /
  `item$set(receiver, ...indices, value)`, its base name being the indexer's metadata name (an
  `[IndexerName("Slot")]` gives `slot$get` / `slot$set`; a `[Name]` replaces it). Two indexers
  of one extension class with the same metadata name (`this[int]` beside `this[string]`, W10-6a)
  report `MS0001` (warning) at each one without a `[Name]`; a `[Name("byKey")]` on such an
  indexer pins its suffix as spec 013 FR-005 pins an overload's (`itemByKey$get`). Each name is
  registered in the project-wide extension-helper registry, so a consumer imports it from the
  class's module and two classes exporting one name report `MS0021`; a consumer of a referenced
  package imports it, like the package's extension methods, from that package (#766: every
  extension-helper call names its container with the container's cross-assembly origin). A read calls the getter, a
  plain assignment the setter. A compound assignment, `++` / `--` and `??=` read the current
  value through the getter and store through the setter, evaluating the receiver and then each
  index once, in source order (an impure one, `xs[i++] += 1`, is assigned to a temporary declared
  at the head of the enclosing statement list, `($t = i++, item$set(xs, $t, item$get(xs, $t) + 1))`,
  with no immediately invoked function, W10-6a; only where no statement list exists, a static
  field initializer, it stays the scoped arrow form, while an instance initializer, a constructor
  statement, declares its temporaries in a block around its store, W10-12); a `Nullable<T>` accessor stores the
  lifted value (`null` stays `null`), a `bool?` `&=` / `|=` / `^=` stores the three-valued
  result a `bool?` field stores, and `??=` calls the setter only when the current value is
  `null` (`item$get(b, i) ?? item$set(b, i, v)`). Where the value of the assignment or increment
  is used, it is the stored value (the previous one for a postfix increment), as in C#. The
  getter may be called again inside a lifted store (getters are assumed side-effect free).
  Tests: `tests/Metano.Tests/ExtensionIndexerTests.cs`, `ExtensionPropertySetterTests.cs`,
  `CrossPackageExtensionAccessorTests.cs`; conformance rows `ExtensionIndexerProbes.*`,
  `CrossPackageExtensionProbes.*`.

- **FR-111** (`IDisposable` and `using`, #800, decision W10-9, TypeScript; amends FR-099's
  `using` declaration source for TypeScript):
  1. `System.IDisposable` maps to the `metano-runtime` interface `IDisposable` with the single
     member `dispose(): void`; `Dispose()` calls lower to `dispose()`. The runtime exports
     `IDisposable$runtimeType`. No `Symbol.dispose` alias is emitted (stable Safari has none, and
     JS `using` wraps a dispose-time error in `SuppressedError`, unlike .NET).
  2. A transpiled class, struct or interface that implements or extends `IDisposable` keeps it
     in its `implements` / `extends` clause and in its descriptor's `interfaces` /
     `baseInterfaces`. `is`, `as`, type patterns, overload dispatch, `typeof` and reified type
     arguments test it nominally, like a transpiled interface: a value with a `dispose` method
     whose class does not implement the interface is not `IDisposable`. The general rule: an
     interface bound by `[ExportFromBcl]` to `metano-runtime` is declared by the runtime, which
     exports `<ExportedName>$runtimeType` beside it (`IrNamedTypeSemantics.IsRuntimeDeclared`);
     an interface a user maps to another package keeps its 3.2.0 behaviour (decision W10-9a).
  3. `using (decl) body`, `using (expr) body` and `using` declarations lower to `try`/`finally`
     that calls `dispose()` on each resource in reverse acquisition order, skips a `null`
     resource (`r?.dispose()`, omitted only for a non-nullable value type or an initializer
     that is an object or collection creation, never from flow state, decision W10-9a), runs
     on every exit (normal, `return`,
     `break`/`continue`, exception, an iterator abandoned early), and lets an exception thrown
     by `dispose()` replace the pending one. An expression resource is held by a temporary
     (`$using0`). A `using` declaration wraps the rest of its scope; several declarators nest
     left to right, so a later initializer that throws still disposes the earlier resources. No
     `Symbol.dispose`, `using` syntax or runtime helper is emitted.
  4. An explicit `IDisposable.Dispose()` is emitted as `dispose()`. When another member of the
     type or a base class already claims that name, it is dropped only if it forwards to that
     member unchanged (one call on `this` passing its parameters in order, to a `public` or
     `internal` member); otherwise `MS0031` is reported.
  5. Not supported (`MS0001`, error): `await using` and `IAsyncDisposable`; a `using` over a
     type whose `Dispose` the target does not emit (a BCL resource such as
     `IEnumerator<T>`); a `using` declaration directly in a module entry point body.
  6. Dart: `using` reports `MS0001`; `IDisposable` is not emitted in heritage clauses.
  Tests: `tests/Metano.Tests/DisposableTests.cs`,
  `targets/js/metano-runtime/test/system/disposable.test.ts`; conformance `DisposableProbes`.

  **Amendment A1 — native `using` and `Symbol.dispose`** (#817, decisions W11-1 and W11-1a,
  TypeScript). Supersedes, for the default configuration, the last sentence of item 1 and the
  lowering of item 3; item 3 stays normative for the resources of A1.2's exceptions and under
  the opt-out of A1.5. Items 2, 4, 5 and 6 are unchanged.
  1. The runtime interface is `IDisposable { dispose(): void; [Symbol.dispose](): void }`.
     Every transpiled class or struct that takes on `IDisposable` itself (directly or through
     an interface, not only by inheriting it from its base class; `IrTypeSemantics.Disposable`,
     with the member that implements `Dispose()`) declares
     `[Symbol.dispose](): void { disposeResource(this, this.<dispose>); }`, where `<dispose>` is
     that member's emitted name (`close` for `[Name("Close")]`). The runtime's
     `disposeResource` only invokes the method; it is the polyfill anchor: building it
     installs `Symbol.dispose ??= Symbol.for("Symbol.dispose")`, and since the runtime never
     imports generated code, the ESM graph evaluates it before any class that reads the
     binding (also within an import cycle of generated modules, and in every bundle), while a
     module whose implementer is unused keeps no load-time code (spec 011 FR-043).
  2. A `using` declaration or statement emits TypeScript `using` declarations
     (`IrVariableDeclaration.IsUsing`, set when `IrExtractionContext.EmitsUsingDeclarations`)
     when its resource's static type is an interface or type parameter, or a class or struct
     whose `IDisposable` contract a type declared and emitted in the compiled project takes on
     (`ResourceDisposal.IsContractTakenOnByThisCompilation`):
     `using var x = e;` → `using x = e;`; `using (T x = e) body` → `{ using x = e; body }`;
     `using (e) body` → `{ using $using0 = e; body }` (the temporary binds the value at entry);
     several declarators → one `using` per declarator, in order. Disposal is in reverse order on
     every exit, a `null` resource is skipped, and a later initializer that throws still
     disposes the earlier resources. Item 3's lowering stays for: an `[Import]` class, or a
     class declared in a referenced package, which may have no alias (also as the class
     constraint of a type-parameter resource); a `ref struct` disposed through its
     pattern-based `Dispose()`. (A `using` declaration cannot be a direct child of a switch
     section, CS8647; a `using` statement there becomes a block.) An interface- or
     type-parameter-typed resource needs a value with `[Symbol.dispose]()`, so instances of a
     referenced package must come from a build of this version. An `[Import]` class of the
     compilation that implements `IDisposable` reports an `MS0001` warning (its instances
     have no `[Symbol.dispose]()` when held through an interface or type parameter); a class
     whose implementing `Dispose()` is `[Emit]` gets no alias and reports `MS0001` (error),
     like an `[Ignore]`d one, and a `using` over it keeps item 3's lowering.
  3. When the body (or a later disposal) and `dispose()` both throw, JavaScript raises a
     `SuppressedError` whose `error` is the `dispose()` exception. `toException`, which every
     generated `catch` and `when` filter reads, unwraps it recursively through `error` (the
     native class and the stand-in TypeScript's downlevel helpers build, recognized by its
     `name`), so the clause sees the exception .NET lets through. An unhandled one reaches the
     host as a `SuppressedError`.
  4. Every consumer of `metano-runtime` needs `ESNext.Disposable` in its tsconfig `lib`: the
     exported `IDisposable` names `Symbol.dispose` (unless `skipLibCheck` hides the runtime's
     declarations and no generated `using` is checked). Below target `ESNext`, TypeScript
     downlevels `using` to its helpers (a per-block allocation, accepted for backward
     compatibility); `importHelpers` with `tslib` is strongly recommended, and a generated
     package that declares a `using` lists `tslib` (`^2.6.0`) as an optional peer dependency
     (`peerDependenciesMeta`), leaving any `tslib` entry it already has as written. The
     samples' tsconfigs list `ESNext.Disposable` and enable `importHelpers`.
  5. Opt-out: MSBuild `MetanoUsingLowering=tryFinally` (`MetanoTsUsingLowering`), CLI
     `--using-lowering tryFinally` (`UsingLowering.TryFinally`), restores the 3.3.0 lowering
     of every `using` (item 3). The output is not byte-identical to 3.3.0: the A1.1 alias, its
     import and the `lib` requirement stay. `native` is the default. This adds
     `UsingLowering` to the FR-086.1 settings list of `Metano.Build.TypeScript`.
  6. Kept from W10-9: `await using` reports `MS0001` (`IAsyncDisposable` stays unmapped, so a
     native `await using` would have no `Symbol.asyncDispose` to call); the module entry point,
     unemitted-`Dispose` and shadowing rules of item 5; the forwarder rules of item 4; Dart.
  Tests: `tests/Metano.Tests/DisposableTests.cs` (goldens `disposable-using-declarations.ts`,
  `disposable-using-forms.ts` under the opt-out), `DisposalHelpersPeerDependencyTests.cs`,
  `test/system/disposable.test.ts`, `test/system/exceptions/to-exception.test.ts`,
  `test/tree-shaking/bundle.test.ts`; conformance `NativeUsingProbes`, `DisposableProbes`,
  `DisposableNullGuardProbes`, `DisposableOverloadProbes` (Bun and Node).
- **FR-111a** (internal descendants and contract members, #819, decision W11-3, TypeScript;
  amends FR-111 and spec 011 D29):
  1. In an `[EmitPackage]` library (or under `[assembly: EmitRuntimeType]`), an internal class
     (reached automatically by W9-9) gets its own `$runtimeType` getter, with its own base,
     interfaces and type parameters, when it has a describable class ancestor that is public or
     reached (decision W11-3a); otherwise it would inherit that ancestor's descriptor and answer
     `is IFoo` wrongly. Any other internal type gets one only when D29 reaches it. Internal types
     stay out of the barrels.
  2. (amended by #821, decision W12-1) An `internal` or `private protected` member that a pure
     forwarder (`void IDisposable.Dispose() => Dispose();`) of a public interface member leans
     on keeps its own visibility tag (`@internal`): it is not widened, so a sibling subclass of
     its base class gains no public member (`stripInternal` keeps `Computed<T>` free of
     `dispose`, as in .NET). The forwarder is not dropped: when the claiming member is inherited
     from a base class, the implementing class declares its own public member under the
     interface member's emitted name, which calls the claiming member through `base`
     (`override dispose(): void { super.dispose(); }`); the `[Symbol.dispose]` alias (item 4)
     stays on that class. A claiming member declared on the implementing class itself cannot
     coexist with a second member of that name, so it is the one member and is emitted public
     and untagged. A claiming member that is already `public` keeps the FR-111 drop. A
     forwarder that is not pure keeps the claiming member public. Known gaps (they keep the 4.0.0
     behavior, the claiming member public and the forwarder dropped): an explicit property or
     event implementation; a forwarder whose return type is not the claiming member's
     (`object ICloneable.Clone() => Clone();`) or whose claiming member is a generic method; an
     abstract claiming member (`super.m()` of an abstract member is TS2513); a claiming member
     that has an overload beside it (the forwarder would join its dispatched group); and two
     forwarders of different interfaces that name the same member, which claim each other
     instead of the inherited member. The forwarder is rebound only from the single statement
     `this.M(...)`; any other published body is reported as an error (MS0001). Dart ignores
     `internal` (`OverloadGroupingPolicy.HidesInternalMembers` is false) and keeps its output.
  3. (#822, decision W14-3; amends the known gaps of item 2) Item 2 also covers a pure
     explicit property or event forwarder (`bool IChangeTracking.IsChanged => IsChanged;`,
     `object? IStrongBox.Value { get => Value; set => Value = value; }`,
     `event Action INote.Changed { add => Changed += value; remove => Changed -= value; }`) to an
     inherited `internal` or `private protected` member of the same type. The claiming member
     keeps its tag, and the implementing class re-publishes it public with no storage of its
     own: an inherited auto-property (one with no per-class storage) as a `declare` field
     (`declare value: Object | null;`), with the inherited mutability, except that a private
     setter the interface does not publish makes it `readonly` (an internal one stays writable on
     the implementing class, the public state the FR-111 path already published); inherited accessors as
     `override` accessors that go through `super`, a setter the interface does not publish
     keeping the inherited visibility (`/** @internal */ override set`); an event as `override`
     `name$add`/`name$remove` methods that call the inherited ones through `super` (the
     custom add/remove accessor error of decision W9-13 does not apply). An indexer forwarder is not
     covered: no indexer is emitted. Every other shape keeps the 4.0.0 path of item 2 (the
     claiming member public, the forwarder dropped) and now reports `MS0001` (warning) at the
     forwarder, naming the reason: an abstract or static claiming member; a forwarder whose
     type is not the claiming member's (a generic method forwarder included); a claiming method
     with an overload beside it; a property forwarder with no getter; a claiming property with
     accessors and an `init` or private setter; a forwarder that is not pure. Two forwarders of
     different interfaces that claim each other report it too (the inherited member then stays
     `internal`). Dart is unchanged. Tests: `tests/Metano.Tests/NarrowedClaimDataMemberTests.cs`;
     conformance `NarrowedClaimProbes` (Bun and Node).
  Tests: `tests/Metano.Tests/InternalRuntimeTypeTests.cs`; conformance `InternalRuntimeTypeProbes`.

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
