# Specification Quality Checklist: System.Text.Json-faithful JSON contracts

**Purpose**: Validate specification completeness and quality before proceeding to tasks
**Created**: 2026-10-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [ ] No [NEEDS CLARIFICATION] markers remain — no markers, but OPEN-1 to OPEN-17 (OPEN-9 closed by measurement) await maintainer confirmation (each has a recommended default)
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- The "users" are developers sharing C# contracts with a TypeScript client. STJ attribute names,
  wire text and runtime class names are the domain vocabulary of the feature (they are what must be
  mirrored), not implementation choices. Generator units, file layout and runtime internals live in
  plan.md, data-model.md and contracts/.
- Every STJ behavior the spec mirrors is backed by a measurement in research.md R1/R4; unmeasured
  corners are open decisions (OPEN-8, OPEN-12, OPEN-15), not assumptions. Known mismatches are
  listed under spec.md "Documented divergences" and marked in the shared conformance manifest.
- Revised after a critic review (base self-registration, abstract derived types, closed generic
  bases and properties, name collisions, cross-package closure, shared manifest, null roots,
  iterables, value-root divergences, unmirrored options, extra root rules, nested polymorphism,
  tag token names, factory corner cases, error classes, overridden properties, type-only imports,
  CI risks, cycles, stale text).
- `tasks.md` is intentionally not written yet: generate it with `/speckit-tasks` once the open
  decisions are settled.
