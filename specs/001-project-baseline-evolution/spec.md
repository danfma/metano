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
  created through one of its emitted constructors or constructor factories, a call to a static
  method or operator it declares, a read or write of a static field or property it declares.
  Its static field and auto-property initializers run first, inside it, in textual order. These
  do not trigger it: reading a `const` or an `[Inline]` field (folded at the use site), a
  reference to the class itself (`typeof`, `instanceof`, passing the class), a nested type, and
  the statics Metano synthesizes (overload selectors, runtime type descriptors). A static member
  reached through a derived type's name triggers only the declaring type (`Derived.M()` with `M`
  declared on `Base` runs `Base`'s), and `new Derived()` runs `Derived`'s before `Base`'s, the
  reverse of the instance constructors. Re-entry, from the body itself or through another type's
  initializer that reads back, sees the type as initialized, with the values assigned so far (C#
  defaults before their initializer). An exception that escapes the initializer is wrapped in a
  `TypeInitializationException` (type name, the original as `InnerException`, .NET's message),
  and every later trigger rethrows that same exception.

  TypeScript lowers a type that declares a static constructor, even an empty one, without a
  `static { }` block. The class's first static member is
  `static readonly #$init = typeInitializer("Ns.Name", () => { … })` (a `metano-runtime`
  helper): its body assigns the field and auto-property initializers in textual order, then runs
  the constructor body. Each static field and auto-property becomes a private `#name` slot that
  holds its default value, plus a `static get name()` / `static set name(value)` pair that calls
  `Name.#$init()` first. A read-only member has no setter. A `static readonly` field or get-only
  static property that the static constructor writes gets a `private` setter, so it stays
  read-only outside the class. Every static method, operator and static property accessor,
  every instance constructor (before `super(...)`) and every constructor factory starts with
  `Name.#$init();`. A type without an explicit static constructor keeps its static field
  initializers at class definition: .NET's `beforefieldinit` leaves their timing to the runtime
  (.NET 10 runs them at the first static field access).

  Differs from .NET or not covered:
  - A generic type runs one initializer for all its constructions, since its static members are
    shared; .NET runs one per closed type.
  - A containerless (`[NoContainer]` / `[ExportedAsModule]`) class keeps its `MS0001` error: an ES
    module variable cannot run code when it is read.
  - `[PlainObject]` emits no class; its static members, the static constructor included, are not
    emitted (unchanged).
  - Dart reports `MS0001` (the body is dropped).
  - An initializer that constructs a class declared in another file which `extends` the type forms
    an import cycle. The initializer no longer runs at load, so the cycle loads when the
    derived class's module is evaluated first, or when both classes are emitted in one file
    (`[EmitInFile]`). When the base class's module is evaluated first, the derived class's
    `extends` clause still reads the base class before it is defined (`ReferenceError`).
  - A static event does not trigger it: static events are not emitted as static members yet.

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
