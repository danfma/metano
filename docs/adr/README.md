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

| ADR                                                              | Title                                                                |
| ---------------------------------------------------------------- | -------------------------------------------------------------------- |
| [ADR-0001](0001-target-agnostic-core.md)                         | Target-agnostic core + per-target projects                           |
| [ADR-0002](0002-handler-decomposition.md)                        | Handler decomposition (not formal GoF Visitor)                       |
| [ADR-0003](0003-declarative-bcl-mappings.md)                     | Declarative BCL mappings via `[MapMethod]` / `[MapProperty]`         |
| [ADR-0004](0004-cross-project-references-via-roslyn.md)          | Cross-project references via Roslyn compilation references           |
| [ADR-0005](0005-inline-wrapper-branded-types.md)                 | `[InlineWrapper]` as branded type + companion namespace              |
| [ADR-0006](0006-namespace-first-barrel-imports.md)               | Namespace-first barrel imports + same-namespace relative             |
