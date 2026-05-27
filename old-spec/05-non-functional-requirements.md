# Non-Functional Requirements

## 1. Convention

The requirements below use identifiers in the format `NFR-XXX`.

## 2. Output Quality

- **NFR-001** Generated TypeScript shall be readable, idiomatic, and close to
  what an experienced developer would write manually.
- **NFR-002** Output shall minimize runtime dependencies and avoid heavy global
  shims.
- **NFR-003** The product shall prioritize zero-cost runtime strategies where
  the transpilation model allows it.

## 3. Semantic Reliability

- **NFR-004** The transpiler shall preserve the essential semantics of
  supported constructs with a high degree of predictability.
- **NFR-005** The system shall not “guess” behavior outside the supported
  surface without explicit signaling.
- **NFR-006** The product shall favor output correctness over artificial
  language coverage.

## 4. Integration and Adoptability

- **NFR-007** Output shall be compatible with the modern TypeScript ecosystem
  without requiring a proprietary runtime environment.
- **NFR-008** The product shall integrate with normal build and packaging
  workflows used by .NET developers.
- **NFR-009** Adoption of Metano shall preserve the user's freedom to choose
  bundlers, test runners, linters, and frontend frameworks.

## 5. Maintainability

- **NFR-010** Internal architecture shall support separation of responsibilities
  between core, target, and build integration.
- **NFR-011** The system shall be evolvable by transformation area without
  requiring global refactors for local changes.
- **NFR-012** The rules and mapping base shall be extensible through
  declarative mechanisms where feasible.

## 6. Observability and Troubleshooting

- **NFR-013** Usage errors, configuration conflicts, and support-boundary
  violations shall be communicated through clear diagnostics.
- **NFR-014** System behavior shall be documented well enough to support
  troubleshooting by both humans and AI tools.

## 7. Performance

- **NFR-015** Transpilation time shall be compatible with use in active
  development workflows.
- **NFR-016** The system shall avoid redundant processing during output
  construction whenever possible.
- **NFR-017** Generated output shall not introduce unnecessary overhead in
  TypeScript consumption.

## 8. Compatibility and Stability

- **NFR-018** The product shall keep output conventions stable unless there is
  a strong reason to introduce change.
- **NFR-019** Breaking changes shall be treated as explicit product evolution
  decisions.
- **NFR-020** Conceptual transpiler contracts shall be traceable through
  documentation, tests, and ADRs.
