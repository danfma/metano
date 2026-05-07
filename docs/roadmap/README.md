# Roadmap

Active and recent execution plans for in-flight work — migrations, multi-PR
sequences, spikes, prioritized cleanups. Roadmap docs are *living* documents:
they evolve as the work progresses and disappear once the work lands or is
abandoned.

## Editorial rule

A document belongs in `docs/roadmap/` while it is **active or recent**. As soon
as its lifecycle changes, the document migrates:

| When the doc... | It moves to... |
|---|---|
| Captures an architectural decision that has stabilized | A new ADR under [`docs/adr/`](../adr/) |
| Becomes a product promise (a feature the system must support) | The relevant section of [`spec/`](../../spec/) |
| Is finished and superseded by code + ADRs | Removed (the git history keeps the trail) |
| Sits idle for ≥ 6 months without any work landing | Archived: either deleted, or moved into the relevant follow-up issue and removed from the tree |

The split exists so each directory has a single, recognisable charter:

- [`spec/`](../../spec/) — *normative* product contract. Stable; changes are
  intentional product decisions.
- [`docs/`](../) — *explanatory* docs for users and contributors.
- [`docs/adr/`](../adr/) — *historical* architectural decisions, immutable
  once accepted.
- `docs/roadmap/` — *active or recent* execution plans, intentionally short-lived.

## What lives here today

| Document | Status | Owner |
|---|---|---|
| [`reorganization.md`](reorganization.md) | Reference brief that motivated [ADR-0019](../adr/0019-compiler-folder-reorganization.md). Preserved for the rationale trail. | — |
| [`better-flutter-support.md`](better-flutter-support.md) | Active plan tracking the Dart/Flutter target gaps. | Dart cluster issues #171–#177 |

## When in doubt

If a new doc could plausibly fit `spec/` or `docs/` or here, prefer the most
specific one. A doc that captures *what we will do next* belongs here. A doc
that captures *what the system must do* belongs in `spec/`. A doc that
explains *how something works today* belongs in `docs/`.
