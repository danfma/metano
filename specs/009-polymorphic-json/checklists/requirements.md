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

- [x] No [NEEDS CLARIFICATION] markers remain — the maintainer settled OPEN-1 to OPEN-17 (now DECIDED-1 to DECIDED-17; DECIDED-9 closed by measurement)
- [ ] New open questions resolved — OPEN-18 (interface-typed derived entries) and OPEN-19 (structural overlap rule versus the C# unions spec) await the maintainer; each has a recommended default
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
  corners are either open (OPEN-18) or scheduled probes (DECIDED-12 in S6, DECIDED-15 in S8), not
  assumptions. The decision round added measurements in research.md R8 (Web defaults, interface
  bases, null roots, `[JsonPropertyOrder]`). Known mismatches are
  listed under spec.md "Documented divergences" and marked in the shared conformance manifest.
- Revised after a critic review (base self-registration, abstract derived types, closed generic
  bases and properties, name collisions, cross-package closure, shared manifest, null roots,
  iterables, value-root divergences, unmirrored options, extra root rules, nested polymorphism,
  tag token names, factory corner cases, error classes, overridden properties, type-only imports,
  CI risks, cycles, stale text).
- Decision round applied: full STJ option set with `[JsonPropertyOrder]` reported by `MS0037`;
  interface and `[PlainObject]` hierarchies supported (synthesized guards on write); full Web
  parity; full null-root parity as a breaking, major release with a migration note; diagnostic
  codes `MS0032`–`MS0037` (the C# unions spec owns `MS0038`–`MS0043`).
- `tasks.md` is intentionally not written yet: generate it with `/speckit-tasks` once OPEN-18 and
  OPEN-19 are settled (their defaults do not block it).
