# Specification Quality Checklist: `long` and `ulong` as native `bigint`

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-04
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (64-bit ids and ticks without precision loss, C# overflow and conversion semantics, working BCL calls, exact JSON, guided migration)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR nodes, emission shapes, runtime helper signatures and JSON descriptor are product requirements, as in specs 005 and 009–011.

## Requirement Completeness

- [x] Maintainer decision applied: always `bigint`, no opt-in, 3.0 major (D1)
- [x] Every C# rule is grounded in a .NET 10 probe line or a C# spec section plus a probe line (research R2, R3); every JS rule in a Node 24.11 / Bun 1.4.2 probe line (R4, R5)
- [x] Every FR names the code it changes (plan.md § Code survey, with line numbers on `90be699e`)
- [x] Requirements are testable (each maps to a golden, a reference fixture, a runtime unit test, a diagnostic test or a document)
- [x] Success criteria are measurable (SC-001–SC-006)
- [x] All acceptance scenarios are defined (US1–US7)
- [x] Edge cases identified (mixed signedness, `MinValue` literal, unary minus / plus, truthiness, boxing, unboxing JS numbers, lifted operators, increments in expression position, array indices, `checked` below 64 bits, shift counts, `BigInteger`, `decimal` fractions, hex literals, JSON trees, `JSON.stringify`, expression trees, unmapped APIs)
- [x] Issue checklist covered: type mapping incl. `nint` / `nuint` and `BigInteger` (FR-001–FR-006); literals and constants (FR-007–FR-009); implicit and explicit conversions incl. `decimal`, `float` / `double` (FR-010–FR-020); overflow with context sources (FR-021–FR-026); operators (FR-027–FR-034); BCL (FR-035–FR-044); runtime equality, hashing, guards, boxing, generics (FR-045–FR-049); JSON incl. #457, #458, numbers from strings, keys, tree precision (FR-050–FR-057); interop, Dart, migration (FR-058–FR-062, DECIDED-19, § Migration); performance (R8, FR-066, NFR-006)
- [x] Scope is clearly bounded (FR-043 and the plan's deferred list)
- [x] Dependencies identified (011 FR-013 with fallback, #457 for one slice, #458 and 009 FR-048 merged)
- [x] Interactions with specs 009 and 011, #457 and #308 listed with the requirement that changes
- [x] First `compiler-man` review (APPROVED WITH NOTES) applied: `uint` / `int` promotion, float → int truncation, staging switch, OQ-3 / OQ-7 re-proposed, Dart narrowing, minor items
- [x] Consistency across spec / plan / contracts / data-model checked (FR-001–FR-073, with FR-069–FR-073 grouped by topic, codes `MS0064`–`MS0066`, slice names 012a–012k, helper names identical in spec and contracts)

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0064`–`MS0066`) in the baseline catalog; next free code `MS0067`
- [x] Spec directory is `specs/012-long-as-bigint/`
- [x] Baseline feature-support matrix rows added (Planned)
- [ ] Open questions OQ-1–OQ-7 answered by the maintainer before `/speckit-tasks` (OQ-3 and OQ-7 carry *Proposed* recommendations revised after the first review)
- [ ] C# spec section numbers cross-checked against the published ECMA-334 text (the behaviors are pinned by probes; only the citations are unverified)
- [ ] ADR for "64-bit integers as `bigint` with core-decided conversions and overflow" (recommended alongside slice 012a; not required by this spec PR)
- [ ] Reference fixtures (FR-064) green from slice 012c on
- [ ] Benchmark (FR-066) recorded before 3.0.0 ships
