# Specification Quality Checklist: BCL collections as runtime types, runtime type descriptors

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (.NET collection semantics, nominal runtime tests, typed catch, zero cost when unused, interop escape hatches)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR, runtime and mapping-layer requirements (FR-001–FR-029) are product requirements, as in specs 005, 008, 009 and 010.

## Requirement Completeness

- [ ] Open decisions resolved. **OD-1–OD-21** are open, each with a recommendation; the maintainer decides before `/speckit-tasks`. OD-10 records a difference between the brief's first and third amendments (checked vs unchecked `dynamic` conversions).
- [x] Every FR is grounded in today's code (research R1–R6 cite the file and line of the behaviour it changes)
- [x] Requirements are testable and unambiguous (each FR maps to a golden, a diagnostic test, a runtime test, a benchmark budget or a documentation file)
- [x] Success criteria are measurable (SC-001–SC-007)
- [x] All acceptance scenarios are defined (US1–US8)
- [x] Edge cases are identified (adopted arrays, erased element descriptors, identity loss, primitives, `NullReferenceException`, non-Error throwables, `params` / optional with hidden parameters, `Type` surface, `new T()`, `default(T)`, `dynamic` operators and naming, `Js.Code` statement form and holes, `UnsafeCast` on .NET, cross-package majors)
- [x] Scope is clearly bounded (deferred: `Target.Language` folding, sorted collections, `Dart.Code`, Dart `Stack` and exception bodies, every-`arr[i]` checks, member metadata, `TypeError` translation)
- [x] Dependencies and assumptions identified (PR #298, specs 009 / 010, #328, #291, #296, #173, #175)
- [x] Consistency across spec / plan / contracts / data-model checked (FR numbers, diagnostic codes, OD numbers, stage mapping)
- [x] No contradiction with specs 009 / 010: the interaction table lists every requirement of theirs that changes and why both remain satisfiable

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0044`–`MS0054`) in the baseline catalog; next free code `MS0055`
- [x] Spec directory is `specs/011-runtime-collections-and-types/`
- [x] Baseline feature-support matrix rows added (Planned)
- [x] ADR-0026 added (own runtime collections + descriptors vs Array subclasses, wrappers with adapters, Deepkit-style transformer)
- [x] Issue created: #335
- [ ] Open decisions resolved with the maintainer before `/speckit-tasks`
- [ ] Benchmark (NFR-001) run and recorded before stage 011b merges
