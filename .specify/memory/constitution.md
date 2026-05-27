<!--
SYNC IMPACT REPORT
==================
Version change: (none) → 1.0.0  [initial ratification]
Bump rationale: First concrete constitution replacing the template placeholders. MAJOR
  baseline because it establishes the governing principle set from scratch.

Principles defined (6):
  I.   Clean Code as the Baseline
  II.  Expressive, Intention-Revealing Code
  III. Screaming, Feature-Semantic Organization
  IV.  Clean Architecture via Ports & Adapters
  V.   Developer Experience First
  VI.  Pragmatism Over Dogma

Added sections:
  - Core Principles (6 principles)
  - Quality Gates & Constraints (was [SECTION_2])
  - Development Workflow (was [SECTION_3])
  - Governance

Removed sections: none (template placeholders fully resolved)

Templates requiring updates:
  ✅ .specify/templates/plan-template.md  — "Constitution Check" gate now resolvable
        against the 6 principles; no structural edit required (gate references
        constitution generically).
  ✅ .specify/templates/spec-template.md  — aligned; no mandatory section conflicts.
  ✅ .specify/templates/tasks-template.md — aligned; principle-driven task categories
        (organization-by-feature, DX/tooling, ports-and-adapters seams) are expressible
        within existing phases.
  ✅ CLAUDE.md — existing conventions (dual-agent review, spec-as-source-of-truth,
        conventional commits, English-only artifacts) are consistent with this constitution.

Follow-up TODOs: none. RATIFICATION_DATE set to first adoption date (2026-05-27).
-->

# Metano Constitution

## Core Principles

### I. Clean Code as the Baseline

Code MUST satisfy Clean Code fundamentals before it is considered done:

- Functions and methods do one thing; small over large. A method that mixes more than one
  level of abstraction MUST be split.
- Names reveal intent. No abbreviations that need a comment to decode, no `tmp`/`data`/`mgr`
  placeholders in committed code.
- No duplicated logic (DRY) and no dead code. Remove it rather than commenting it out.
- Complex boolean conditions in `if`/`while` heads MUST be extracted into named predicates.
- Blank-line and spacing discipline around multi-line statements is part of the diff, not an
  afterthought.
- All committed C# MUST pass `dotnet csharpier .`; warnings are errors (`TreatWarningsAsErrors`).

**Rationale**: Metano is a compiler — its own source is read far more often than written.
Clean Code is the floor, not the ceiling, because correctness review is only possible on code
that can be read quickly.

### II. Expressive, Intention-Revealing Code

The code MUST read like the domain it models, not like the mechanics that implement it:

- Domain vocabulary (transpile, target, lowering, emit, rewriter, IR, symbol) MUST appear in
  type, method, and file names. The reader learns the system by reading its names.
- Prefer types that make illegal states unrepresentable over runtime guards.
- A new contributor MUST be able to infer what a unit does from its public surface without
  reading the body. When that is impossible, the design — not a comment — MUST be reworked.

**Rationale**: Expressiveness is the cheapest documentation. It compounds: every well-named
seam reduces the cost of the next change.

### III. Screaming, Feature-Semantic Organization

Folders and namespaces MUST scream what the system *does*, not merely which technical layer a
file belongs to:

- Organize primarily by feature / semantic capability (e.g. `Transformation/`, `Bridge/`,
  `Diagnostics/`, `Runtime/`), not by generic buckets like `Helpers/`, `Managers/`, or `Utils/`.
- A purely layer-named container (e.g. a catch-all `Services/` or `Models/`) is a smell and
  MUST be justified or replaced with a capability-named one.
- Co-locate the things that change together. When a feature spans multiple files, their names
  and locations MUST make the grouping obvious.
- The directory tree alone MUST let a reader guess the feature set before opening any file.

**Rationale**: Screaming Architecture turns the file tree into a map of the product. It directly
fights shotgun surgery by keeping each capability's pieces discoverable in one place.

### IV. Clean Architecture via Ports & Adapters

Dependencies MUST point inward toward a target-agnostic core:

- The compiler core (`Metano.Compiler`) MUST NOT depend on any concrete language target. Targets
  (TypeScript, Dart, future Kotlin) depend on the core through the `ITranspilerTarget` port — never
  the reverse.
- Every external concern (a language target, a printer, a file writer, a BCL mapping source) sits
  behind an interface (a port) with concrete adapters. Swapping or adding an adapter MUST NOT
  require editing the core.
- Cross-cutting policy (symbol resolution, IR shape, diagnostics) lives in the core; emission and
  language-specific quirks live in the adapter.

**Rationale**: A multi-target transpiler only stays maintainable if adding the next target is an
additive adapter, not a core rewrite. Ports & Adapters is the structural guarantee of that.

### V. Developer Experience First

The project MUST be fast and obvious to work in:

- A single documented command MUST build, and a single documented command MUST run the relevant
  tests, for each toolchain (.NET via `dotnet run`, JS/TS via Bun). New surfaces ship with the
  command to exercise them.
- Diagnostics MUST be actionable: a stable code (MS0001–), a clear message, and enough location
  to fix the problem. Silent failure is prohibited.
- Golden/expected-output tests MUST accompany transpiler behavior so feedback on regressions is
  immediate and concrete.
- Tooling is fixed, not negotiated per file: Bun (never npm/yarn/pnpm) for JS, CSharpier for C#,
  Mermaid for diagrams, English for all committed artifacts.

**Rationale**: DX is a force multiplier. Slow or surprising feedback loops silently erode every
other principle by making the right thing harder than the quick thing.

### VI. Pragmatism Over Dogma

Architecture serves the code; the code does not serve the architecture:

- Do NOT introduce a pattern, layer, abstraction, or indirection without a present, named need.
  Speculative generality (YAGNI violations) MUST be rejected in review.
- Apply the lightest structure that keeps the other principles true. A port with exactly one
  adapter and no foreseeable second one is a candidate for inlining, not a goal in itself.
- When a principle here pulls against a concrete, demonstrated need, the deviation MUST be recorded
  in the plan's Complexity Tracking (and, for architectural choices, an ADR) with the simpler
  alternative and why it was rejected.

**Rationale**: Forcing Clean/Hexagonal/Screaming structure "for its own sake" produces the same
unreadable code it claims to prevent. Principles I–V are means to expressive, changeable code —
this principle keeps them from becoming ceremony.

## Quality Gates & Constraints

- **Spec as source of truth.** Every feature MUST trace to a functional requirement (FR-NNN) or
  non-functional requirement (NFR-NNN) in the canonical speckit specification under
  `specs/001-project-baseline-evolution/` (the legacy `spec/` was migrated and renamed `old-spec/`,
  kept only as a comparison reference pending retirement). New behavior without an FR requires a spec
  change first; implemented-but-unspecified behavior is documentation debt that MUST be reconciled.
- **Dual-agent review before commit.** `compiler-man` (semantic correctness, AST/IR shape, lowering,
  pipeline coverage) and `bob` (Clean Code, naming, method size, condition complexity, organization)
  MUST both review non-trivial changes before they are committed. Findings are fixed before commit.
- **Build & tests green.** A change MUST NOT be declared complete unless the solution builds and the
  relevant .NET (TUnit) and TS (bun:test) suites pass. Failures are reported with output, not hidden.
- **Decisions are traceable.** Architectural choices are captured as MADR-style ADRs under
  `docs/adr/`; concrete work is tracked in GitHub issues referencing the spec.

## Development Workflow

- **Worktree per issue.** Use `git worktree add ../Metano-issue-{N} -b <branch> main`. Never switch
  branches in the main working directory — other agents may be working there concurrently.
- **Conventional commits.** `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:` with optional
  scopes and `!` for breaking changes. Descriptions start with an infinitive verb (`add`, `correct`,
  `move`). Reference the issue with `(#N)` in the title and `Closes #N` / `Part of #N` in the body.
- **No AI attribution in commits** unless a project explicitly allows it.
- **English everywhere** in committed artifacts (code, comments, docs, commits, PRs). Conversations
  may be in Portuguese.
- **Plan before non-trivial work.** Tasks of 3+ steps or with architectural impact get a plan; the
  plan's Constitution Check gate MUST be evaluated against the principles above before implementation.

## Governance

This constitution supersedes ad-hoc practice. When a rule here conflicts with habit, this document wins.

- **Amendments** are made by editing this file via a tracked PR that includes the updated Sync Impact
  Report and bumps the version. Amendments that change how work is governed require the same
  dual-agent review applied to code.
- **Versioning policy** (semantic):
  - MAJOR — backward-incompatible governance changes: removing or redefining a principle.
  - MINOR — adding a principle/section or materially expanding guidance.
  - PATCH — clarifications, wording, and non-semantic refinements.
- **Compliance review.** Plans and PRs MUST verify compliance with these principles. Any deviation MUST
  appear in the plan's Complexity Tracking with justification and the rejected simpler alternative;
  unjustified complexity blocks merge.
- **Runtime guidance.** `CLAUDE.md` provides day-to-day operational guidance for contributors and agents
  and MUST stay consistent with this constitution; on conflict, the constitution governs and `CLAUDE.md`
  is corrected.

**Version**: 1.0.0 | **Ratified**: 2026-05-27 | **Last Amended**: 2026-05-27
