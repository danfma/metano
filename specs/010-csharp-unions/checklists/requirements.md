# Specification Quality Checklist: C# 15 union declarations

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (erased TS types, dispatch that matches C#, STJ wire parity)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR and toolchain requirements (FR-001–FR-011) are product requirements, as in specs 005 and 008.

## Requirement Completeness

- [x] Open decisions resolved: **OD-1–OD-17** are all DECIDED in spec.md. The maintainer decided OD-3, OD-5, OD-9 and OD-11 against the earlier recommendation (D10–D13, FR-039–FR-042); the others adopt the recommendation.
- [ ] No open questions remain. **OQ-1** (a union converted to its extra interface) and **OQ-2** (a cross-assembly structural case without a producer guard) are new and explicit; each has a v1 default that the spec already applies (MS0038 and MS0040), so neither blocks a stage. **OQ-3** (FR-021 versus the switch-statement lowering that #239 merged on `main`) blocks only the switch-statement part of stage 010c.
- [x] Spec-review gaps addressed (2026-10-31): JSON closure over union cases, labeled-block switch lowering, constructed-generic validation, value-domain overlap, guard ownership and imports, dispatcher coverage, relational patterns, hand-written unions, `IUnion` access, net11 test references, CI pinning, STJ option/token parity, cross-package JSON, default-value divergences, equality, nested unions and Dart details. New probes are recorded in research R10 and R11.
- [x] Requirements are testable and unambiguous (each FR maps to a golden, a diagnostic test or a fixture)
- [x] Success criteria are measurable (SC-001–SC-006)
- [x] All acceptance scenarios are defined (US1–US7)
- [x] Edge cases are identified (default and its widening, boxing, value semantics, overlapping cases, nested unions, generics, `Nullable<U>.Value`, synthesized guards, the alias/namespace merge)
- [x] Scope is clearly bounded (deferred: union JSON roots, polymorphic cases, adapters for the extra-interface conversion, consumer-local guards, a Dart companion, open-`T` tests)
- [x] Dependencies and assumptions identified (PR #298, merged; spec 009-polymorphic-json for #268 / #270; SDK 11 RC)

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0038`–`MS0043`) in the baseline catalog. History: first drafted as `MS0031`–`MS0036`, renumbered to `MS0032`–`MS0037` after #278 took `MS0031`, then moved to `MS0038`–`MS0043` because spec 009-polymorphic-json owns `MS0032`–`MS0037`.
- [x] Spec directory is `specs/010-csharp-unions/` (spec 009 is polymorphic JSON)
- [x] Baseline feature-support matrix rows added (Planned)
- [x] Issue created: #304
- [x] Open decisions resolved with the maintainer before `/speckit-tasks`
