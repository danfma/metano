# ADR-0019 — `Metano.Compiler` folder split: `IR` is data, `Analysis` is behavior, `Mappings` is shared schema, `Frontend/Roslyn` is migration debt

**Status:** Accepted
**Date:** 2026-05-07

## Context

`Metano.Compiler` had grown an organic mix of folders that did not match
their actual concerns:

- `IR/` held both pure data records (modules, type declarations,
  expressions) **and** behavior — `IrEqualityClassifier` (analysis)
  and Roslyn-tainted records like `IrTranspilableTypeEntry` (which
  carries `INamedTypeSymbol`).
- `Extraction/` was the home for Roslyn → IR extractors, but it also
  hosted `IrRuntimeRequirementScanner`, which walks already-extracted
  IR (no Roslyn involvement).
- `DependencyGraph/` was a single-class folder for
  `IrTypeDependencyGraph`, with no other neighbours and no clear
  promotion path.
- `Transformation/` in core held a single file —
  `DeclarativeMappingRegistry.cs` — that declared
  `namespace Metano.Transformation;` (no `Compiler` segment, sharing
  the namespace with TS-target transformation files in a different
  project). The mismatch tripped both "go to definition" and casual
  folder-tree reading.

The rogue `Metano.Transformation` namespace was used by 42 files
across the core, the TypeScript target, the Dart target, and the
test suite — so the cleanup had to be a coordinated split, not a
single-file move.

## Decision

Split `Metano.Compiler` along the actual axes the code lives on.
The folder names line up with what each member *is*, and the rule
"folder + namespace + project name agree" is now invariant:

- **`IR/`** holds pure IR data records only. No behavior, no Roslyn
  symbols. Anything that walks IR or reaches into Roslyn moves out.
- **`Analysis/`** absorbs every pure walker over already-extracted
  IR — `IrRuntimeRequirementScanner`, `IrEqualityClassifier`, and
  `IrTypeDependencyGraph` (the last had its own one-class folder
  before; consolidating signals "these have the same job"). The
  cache (#21), watch (#18), and per-target bridges all consume the
  same `Analysis/` surface.
- **`Mappings/`** carries the declarative BCL mapping schema —
  `DeclarativeMappingRegistry` (target-shared registry, was
  mis-namespaced in `Transformation/`) and `DeclarativeMappingEntry`
  (was in `IR/` despite carrying target-specific slots). Both now
  sit in `Metano.Compiler.Mappings`, multi-target by design (a
  future Kotlin target adds `KotlinName` to `DeclarativeMappingEntry`
  without touching `IR/`).
- **`Frontend/Roslyn/`** contains the IR records that still carry
  Roslyn symbols — `IrTranspilableTypeEntry` and
  `IrEntryPointInfo`. These are *intentional* migration debt: per-
  target bridges still need `INamedTypeSymbol` for body-level walks
  the IR does not yet cover. Burying them under
  `Frontend/Roslyn/` makes the leak visible at the path level so a
  contributor cannot accidentally reach for them without noticing.
  When the IR migration completes, the folder empties out and goes
  away.

The TypeScript and Dart targets keep their own `Transformation/`
folders (`Metano.Compiler.TypeScript.Transformation`,
`Metano.Compiler.Dart.Transformation`) — those files are
target-specific and do not belong in the core.

While doing the move we also normalized every namespace under
`Metano.Compiler.*` so the .NET convention "namespace starts with
the project root" actually holds:

- `Metano.TypeScript` / `Metano.TypeScript.AST` /
  `Metano.TypeScript.Bridge` →
  `Metano.Compiler.TypeScript` / `.AST` / `.Bridge`.
- `Metano.Dart` / `Metano.Dart.AST` / `Metano.Dart.Bridge` /
  `Metano.Dart.Transformation` →
  `Metano.Compiler.Dart.*`.

The user-facing `Metano.Annotations` namespace stays untouched —
it lives under the `Metano` package on purpose so consumer code
only depends on the short form.

The `IR` namespace casing stays. Microsoft's framework guidelines
flag the choice (`IR` vs `Ir`), but the codebase (and adjacent
projects like LLVM and Roslyn) treats `IR` as the established
acronym, and renaming touches every consumer in both targets and
all tests for zero semantic gain.

## Consequences

(+) Folder names answer "what lives here?" honestly. New
   contributors can map a member's job to its location without
   reading the file first.
(+) `IR/` becoming records-only protects the namespace from drift —
   anything `Ir*` plus behavior now has a clear home (`Analysis/`),
   and reviewers can flag reverse drift on sight.
(+) The Roslyn migration debt gets a dedicated folder instead of a
   stale doc comment. Contributors stop reaching for the escape
   hatches without realising it; the debt is self-documenting via
   the path.
(+) Cross-target schema (`DeclarativeMappingEntry`) sits in a folder
   whose name names that intent (`Mappings/`), not in `IR/` where
   the per-target slots looked out of place.
(+) The orphan `Metano.Transformation` namespace is gone. All
   `using` lines now point at one of:
   `Metano.Compiler.Mappings`,
   `Metano.Compiler.TypeScript.Transformation`,
   `Metano.Compiler.Dart.Transformation`. Each name says where the
   members live, and the consumer's own namespace tells you which
   ones it should reach for.
(−) One-time `using`-line churn: ~30 files needed the new namespace
   added (or split into two, when they previously pulled
   `Metano.Transformation` for both registry and TS-only helpers).
   Mechanical, but a reviewer needs to skim the diff carefully.
(−) `Frontend/Roslyn/` advertises tech debt at the folder level.
   That is the point — but it also creates a small import-path
   embarrassment that may stay around longer than expected if the
   IR migration drags. We accept that visibility cost on purpose.

## Alternatives considered

- **Keep everything in `IR/`.** Rejected: the folder was already
  drifting (analysis + Roslyn-tainted records mixed with pure
  data). Without a split it would have continued absorbing
  everything `Ir*`, defeating the namespace as a useful
  category.
- **Split `Analysis/` only when a fourth helper appeared.**
  Rejected: three helpers (`IrEqualityClassifier`,
  `IrRuntimeRequirementScanner`, `IrTypeDependencyGraph`) were
  already enough that two of them lived in misleading folders
  (`IR/`, `Extraction/`). Waiting for a fourth meant tolerating
  the misnomer in the meantime.
- **Hide the Roslyn escape hatches behind comments instead of
  folder names.** Rejected: comments rot; folder paths do not.
  The whole point of `Frontend/Roslyn/` is that a reviewer skims
  the path and pauses.
- **Rename `IR` namespace to `Ir`.** Rejected: high churn (every
  consumer in both targets and tests), zero semantic gain. The
  acronym is established and reads naturally next to type names
  (`IrModule`, `IrTypeRef`).
- **Merge `docs/`, `docs/adr/`, and `plans/` into one tree.**
  Rejected: three audiences (contributors, history readers,
  active-roadmap maintainers) want three top-level entry points.
  The reorganization moves `plans/reorganization.md` to
  `docs/roadmap/` and the existing `docs/better_flutter_support_plan.md`
  to `docs/roadmap/` too, but keeps the three root directories.

## References

- `src/Metano.Compiler/Analysis/`
- `src/Metano.Compiler/Mappings/`
- `src/Metano.Compiler/Frontend/Roslyn/`
- `src/Metano.Compiler/IR/` (now records-only)
- ADR-0001 — target-agnostic core + per-target projects
- ADR-0002 — handler decomposition (TS-target `Transformation/`
  references unchanged)
- ADR-0013 — shared IR as canonical semantic representation
- ADR-0018 — type-level dependency graph (now under `Analysis/`)
- `docs/roadmap/reorganization.md` — the planning brief that
  motivated this ADR
