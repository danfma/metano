# Specification Quality Checklist: BCL collections as runtime types, runtime type descriptors

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01 (revised after the first and second critic passes)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Focused on user value (.NET collection semantics, nominal runtime tests, typed catch, zero cost when unused and measured, interop escape hatches)
- [x] All mandatory sections completed
- [ ] No implementation details. *Intentional deviation*: Metano is a compiler, so the IR, runtime, emission-shape and mapping-layer requirements (FR-001–FR-029, FR-043, FR-044) are product requirements, as in specs 005, 008, 009 and 010. The emission shapes are normative because tree-shaking depends on them (research R13).

## Requirement Completeness

- [ ] Open decisions resolved. **OD-1–OD-22** are open with a recommendation each, except **OD-10**, which the maintainer resolved in the first critic pass (implicit `dynamic` conversions unchecked, explicit casts checked when answerable).
- [x] Every FR is grounded in today's code (research R1–R6 cite the file and line of the behaviour it changes) or in a measurement (R13 for every emission shape, R14 for equality)
- [x] Requirements are testable and unambiguous (each FR maps to a golden, a diagnostic test, a runtime test, a benchmark budget, a bundle budget or a documentation file)
- [x] Success criteria are measurable (SC-001–SC-008)
- [x] All acceptance scenarios are defined (US1–US9)
- [x] Edge cases are identified (fixed-size adopted arrays and `asArray`, erased element descriptors, identity loss, duplicated package copies, primitives, `NullReferenceException`, non-Error throwables and `AbortError`, catch-filter ordering, `params` / optional with hidden parameters, overloads vs override groups, `Type` surface, `new T()`, `default(T)`, `dynamic` operators and verbatim naming on Metano objects, `Js.Code` statement form and string-literal holes, `UnsafeCast` on .NET, enumeration invalidation, dictionary order after removals, cross-package majors)
- [x] Scope is clearly bounded (deferred: `Target.Language` folding, sorted collections, `Dart.Code`, Dart `Stack` and exception bodies, every-`arr[i]` checks, member metadata, `TypeError` translation, flat exports for pre-existing namespaces (#361) and union companions)
- [x] Dependencies and assumptions identified (PR #298, specs 009 / 010, #328, #334 / #355 for `MS0044`, #361, #291, #296, #173, #175, the measured bundler versions)
- [x] Consistency across spec / plan / contracts / data-model checked (FR numbers ascending, diagnostic codes `MS0045`–`MS0055`, OD numbers, stage order 011a–011g with 011b before 011c, Dart mapping in every stage)
- [x] No contradiction with specs 009 / 010: the interaction table lists every requirement of theirs that changes (including 010 D13 / FR-042 companions, FR-017 / US7-7, ADR-0009's registry, the `$type` rename and duplicated-package identity) and why both remain satisfiable

## Feature Readiness

- [x] Diagnostic codes reserved (`MS0045`–`MS0056`) in the baseline catalog; `MS0044` left to #334 / PR #355; next free code `MS0057`
- [x] Spec directory is `specs/011-runtime-collections-and-types/`
- [x] Baseline feature-support matrix rows added (Planned)
- [x] ADR-0027 added (own runtime collections + descriptors vs Array subclasses, wrappers with adapters, Deepkit-style transformer, symbol-keyed statics / namespaces, a guard registry, structural equality); `0026` stays reserved by spec 010
- [x] Issue created: #335; follow-ups #361 (pre-existing namespace emissions) and #368 (flat `sideEffects: false`) filed, both on the 3.0.0 release gate
- [ ] Open decisions resolved with the maintainer before `/speckit-tasks`
- [ ] Benchmark (NFR-001) run and recorded before stage 011c merges
- [ ] Tree-shaking suite (FR-043) green from stage 011a on, with its side-effect self-test proving the check is not vacuous
- [ ] FR-043 allowlist empty before 3.0.0 ships (#361 landed)
