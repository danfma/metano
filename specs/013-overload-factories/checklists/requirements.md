# Specification Quality Checklist: Overload factories, selector symbols and direct call sites

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-04
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (addressable constructors, dispatcher-free hot paths, predictable names, guided migration)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR fields, emission shapes, the naming algorithm and the diagnostic are product requirements, as in specs 005 and 009–012.

## Requirement Completeness

- [x] Maintainer decisions applied: constructor factories, selector symbols, direct call sites, shared method scheme, one naming algorithm, 3.0.0 break, documented name stability (D1–D7)
- [x] Every TypeScript claim is grounded in a probe line (research R1–R5: strict + declaration acceptance, `.d.ts` shapes, TS2445, TS2511, Node and Bun runs); today's output verified by running the transpiler (R7)
- [x] Every FR names the code it changes (plan.md § Code survey, with line numbers on `5c084f50`)
- [x] Requirements are testable (each maps to a golden, a unit test, a diagnostic test, a bun test or a document)
- [x] Success criteria are measurable (SC-001–SC-006)
- [x] All acceptance scenarios are defined (US1–US6)
- [x] Edge cases identified (single overload, zero-arg, all-optional / `params`, mixed accessibility, widening, abstract classes, nested types, `[PlainObject]` / branded drops, companion exports, cross-package, `this(...)` chains, generics, `nameof`, dynamic receivers, record synthesis)
- [x] Issue checklist covered: constructor factories (D1, FR-007, FR-011, FR-014); selector symbols incl. description, derived `super` and the fallback (D2, FR-008–FR-010); direct call sites for `new`, `base(...)` and methods (D3, FR-017–FR-019); public fast paths with a visibility proposal (D4 + DECIDED-6); naming rules 1–6 incl. the joiner checked against `ComputeFastPathNames` (D5, FR-001–FR-005, contracts/naming.md); 3.0.0 classification (D6, Migration); name stability (D7, Migration item 7)
- [x] Scope is clearly bounded (FR-013's excluded shapes, DECIDED-10/11, the plan's deferred list)
- [x] Dependencies identified (#445 as the start gate, FR-049's accessibility predicate, ADR-0008/0009/0013, 012 OQ-6)
- [x] Interactions with ADR-0008 (superseded in part by the new ADR-0028), #445, #438/#440, spec 010's companion namespace and spec 005's layout listed
- [x] Consistency across spec / plan / contracts / data-model checked (one canonical example family `Dimension` / `Money` / `Shapes`; suffix names identical in naming.md, ts-emission.md, data-model.md and quickstart.md; slice names 013a–013f; code `MS0067`)

## Feature Readiness

- [x] Diagnostic code reserved (`MS0067`) in the baseline catalog; next free code after it is `MS0068`
- [x] Spec directory is `specs/013-overload-factories/`
- [x] Baseline feature-support matrix row added (Planned)
- [x] Open questions OQ-1–OQ-5 each carry a recommendation; the plan proceeds on the recommendations
- [ ] Maintainer signs off OQ-1 (`[Name]` scope) before slice 013e
- [ ] ADR-0028 reviewed alongside slice 013a (written with this spec PR)
- [ ] #445 merged before slice 013b starts
- [ ] `MS0067` tests green from slice 013e on
