# Specification Quality Checklist: C# 15 union declarations

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (erased TS types, dispatch that matches C#, STJ wire parity)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR and toolchain requirements (FR-001–FR-011) are product requirements, as in specs 005 and 008.

## Requirement Completeness

- [ ] No open decisions remain. **OD-1–OD-12** are listed in spec.md, each with a recommendation; none blocks stage 009a.
- [x] Requirements are testable and unambiguous (each FR maps to a golden, a diagnostic test or a fixture)
- [x] Success criteria are measurable (SC-001–SC-005)
- [x] All acceptance scenarios are defined (US1–US6)
- [x] Edge cases are identified (default, boxing, value semantics, nested unions, generics, `Nullable<U>.Value`)
- [x] Scope is clearly bounded (deferred: union JSON roots, polymorphic cases, body members, open-`T` tests)
- [x] Dependencies and assumptions identified (PR #298, #268, the non-object JSON root fix, SDK 11 RC)

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0031`–`MS0036`) in the baseline catalog
- [x] Baseline feature-support matrix rows added (Planned)
- [x] Issue created: #304
- [ ] Open decisions resolved with the maintainer (`/speckit-clarify`) before `/speckit-tasks`
