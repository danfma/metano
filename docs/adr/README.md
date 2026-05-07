# Architecture Decision Records

This directory captures the architectural decisions that shaped Metano. Each
ADR is a short [MADR](https://adr.github.io/madr/)-style document answering
*why* a particular choice was made, written at the moment it was made (or
retroactively when the decision predates this log). Features and tickets
live in GitHub issues; ADRs are for the decisions a future reader might
second-guess when they meet the code without the context that led to it.

## Conventions

- Filenames: `nnnn-slug.md`, four-digit zero-padded, sequential.
- Numbers are permanent. If an ADR is superseded, the replacement gets a
  new number and the original is marked `Superseded by ADR-NNNN`.
- Prefer short ADRs. Use the [template](template.md) and keep each section
  to what is actually load-bearing.
- Ground every decision in concrete references (file paths, issue numbers,
  commits). An ADR that can't be traced back to the code is fiction.

## Index

| ADR                                                              | Title                                                                          |
| ---------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| [ADR-0001](0001-target-agnostic-core.md)                         | Target-agnostic core + per-target projects                                     |
| [ADR-0002](0002-handler-decomposition.md)                        | Handler decomposition (not formal GoF Visitor)                                 |
| [ADR-0003](0003-declarative-bcl-mappings.md)                     | Declarative BCL mappings via `[MapMethod]` / `[MapProperty]`                   |
| [ADR-0004](0004-cross-project-references-via-roslyn.md)          | Cross-project references via Roslyn compilation references                     |
| [ADR-0005](0005-inline-wrapper-branded-types.md)                 | `[InlineWrapper]` as branded type + companion namespace                        |
| [ADR-0006](0006-namespace-first-barrel-imports.md)               | Namespace-first barrel imports + same-namespace relative                       |
| [ADR-0007](0007-output-conventions.md)                           | Output conventions: kebab-case, leaf-only barrels, `#/` alias, `sideEffects`   |
| [ADR-0008](0008-overload-dispatch.md)                            | Overload dispatch: slow-path dispatcher + fast-path private methods            |
| [ADR-0009](0009-type-guards.md)                                  | Type guards as shape validation with `instanceof` fast path                    |
| [ADR-0010](0010-metano-diagnostics.md)                           | `MetanoDiagnostic` + MS0001–MS0024 codes                                       |
| [ADR-0011](0011-emit-package-ssot.md)                            | `[EmitPackage]` as single source of truth for `package.json#name`              |
| [ADR-0012](0012-linq-eager-wrapper.md)                           | LINQ as eager wrapper hierarchy (with pipe-based migration tracked)            |
| [ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md) | Shared IR as the canonical semantic representation                           |
| [ADR-0014](0014-loose-null-equality-in-generated-typescript.md)  | Loose null equality in generated TypeScript                                    |
| [ADR-0015](0015-attribute-family-for-compile-time-erasure.md)    | Attribute family for compile-time erasure                                      |
| [ADR-0016](0016-this-attribute-bindreceiver.md)                  | `[This]` attribute + `bindReceiver` runtime helper                             |
| [ADR-0017](0017-no-container-and-explicit-inline-mode.md)        | `[NoContainer]` + explicit inline mode                                         |
| [ADR-0018](0018-type-level-dependency-graph.md)                  | Type-level dependency graph as the backbone for incremental + watch            |
| [ADR-0019](0019-compiler-folder-reorganization.md)               | `Metano.Compiler` folder split: IR / Analysis / Mappings / Frontend.Roslyn     |
| [ADR-0020](0020-parallel-type-transformer.md)                    | Parallel `TypeTransformer` per file group + thread-safe shared state           |
