# Specification Quality Checklist: Overload factories, selector symbols and direct call sites

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-04 · **Revised**: 2026-11-04 (round 2)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (addressable constructors, dispatcher-free hot paths that bind like C#, correct virtual dispatch, predictable names, guided migration)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR fields, emission shapes, the naming algorithm and the diagnostics are product requirements, as in specs 005 and 009–012.

## Requirement Completeness

- [x] Maintainer decisions applied: D1–D7 (round 1) and the round-2 decisions on virtual calls (DECIDED-15), type-qualified selectors (DECIDED-16) and per-overload visibility (DECIDED-17), each propagated to every FR, contract, example and planned golden
- [x] Review findings applied: Roslyn-captured spellings and the parameter-type climb (DECIDED-1, DECIDED-4); target-side claims with one order and per-suffix `$` (DECIDED-18); selectors first plus guarded branches (DECIDED-19); the #475 carve-out and 012 FR-048 `…UInt64` interaction; complete Migration (a)–(i); project-reference-only cross-package calls (DECIDED-24); struct `new S()`, `params`, extension overloads, `MS0003` dead end, JSON factory, generic constraints
- [x] Every TypeScript claim is grounded in a probe line (research R1–R13), each run under `tsgo` and Node (Bun where load order matters); today's output verified by running the transpiler on `f520be36` (R7, R8, R12)
- [x] Every FR names the code it changes (plan.md § Code survey)
- [x] Requirements are testable (each maps to a golden, a unit test, a diagnostic test, a bun / Node run or a document)
- [x] Success criteria are measurable (SC-001–SC-007)
- [x] All acceptance scenarios are defined (US1–US7)
- [x] Edge cases identified
- [x] Baseline traceability: FR-050 (overload dispatch) added to the baseline spec and cited by the matrix rows; stale "FR-042 (overload dispatch)" references dropped; the 010 FR-042 row labeled unrelated
- [x] Scope is clearly bounded (FR-013's excluded shapes, DECIDED-11, DECIDED-24, the plan's deferred list)
- [x] Dependencies identified (#445 as the gate for 013b and 013d; FR-049; #475 related, not a prerequisite)
- [x] Consistency across spec / plan / contracts / data-model checked (one example family: `Dimension`, `Money` / `Receipt` / `GiftReceipt`, `Cash`, `Base` / `Derived`, `IShape` / `Sq`, `Shapes`, `Extra`; slice names 013a–013g; codes `MS0067`, `MS0068`)

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0067`, `MS0068`) in the baseline catalog; next free code `MS0069`
- [x] Spec directory is `specs/013-overload-factories/`
- [x] Baseline feature-support matrix row updated (Planned)
- [x] ADR-0028 is Proposed; ADR-0008 carries the "superseded in part" note
- [x] Open questions OQ-1 and OQ-3–OQ-8 each carry a recommendation (OQ-2 closed by DECIDED-17)
- [ ] Maintainer signs off OQ-1 (`[Name]` scope) before slice 013f, and OQ-7 before slice 013d
- [ ] #445 merged before slices 013b and 013d start
- [ ] `MS0067` tests green from slice 013b on, `MS0068` from 013d
