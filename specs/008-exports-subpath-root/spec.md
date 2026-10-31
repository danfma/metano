# Feature Specification: Opt-in exports subpath root

**Feature Branch**: `feat/267-exports-subpath-root` (spec directory `008-exports-subpath-root`)

**Created**: 2026-10-31

**Status**: Draft

**Input**: GitHub issue [danfma/metano#267](https://github.com/danfma/metano/issues/267) — "Allow an exports subpath root so package subpaths do not repeat the package identity".

**Traceability**: Spec change request that **amends FR-002 and FR-013 of
[`specs/005-deterministic-output-layout`](../005-deterministic-output-layout/spec.md)** and
extends the baseline `[EmitPackage]` capability (feature-support-matrix "Packaging" row,
FR-030 family). The 005 layout itself (D1 full-namespace folders, D2 barrels, D3 direct
internal imports, pruning) is unchanged.

## Overview

Under the full-namespace layout (005 D1) a package's `exports` keys are the full kebab-cased
namespace of each leaf barrel. When the namespace already carries the package identity, the
subpath repeats it: the package `@video-studio/domain`, built from `VideoStudio.Domain.*`,
exports `./video-studio/domain/episodes`, so consumers write
`import { Episode } from "@video-studio/domain/video-studio/domain/episodes"`.

This feature lets the producing assembly declare an **exports subpath root**: a namespace
prefix that is stripped from the `package.json#exports` keys only, so the consumer writes
`@video-studio/domain/episodes`. Everything that 005 made deterministic stays as it is: files
remain under `src/video-studio/domain/episodes/…`, internal imports and the `#/*` alias are
untouched, and the stripping is a pure function of the declared root and each barrel's own
namespace (never of the set of types present).

## Why a separate feature directory (and not an in-place edit of 005)

005 is a shipped, self-contained feature whose requirements, contracts and tests describe one
decision (D1, "no stripping"). This change is an opt-in refinement of only the package-facing
surface. Recording it as its own small feature keeps 005's history readable, gives the new
rules (matching, collisions, diagnostics) a home, and lets 005 point here with a one-line
amendment note on FR-002/FR-013 and its import contract.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Short package subpaths for a producer (Priority: P1)

A library author whose namespaces already start with the package identity declares
`[assembly: EmitPackage("@video-studio/domain", SubpathRoot = "VideoStudio.Domain")]`. The
generated `package.json#exports` exposes `./episodes` instead of
`./video-studio/domain/episodes`, while the files stay where 005 puts them.

**Independent Test**: Write `package.json` for barrels `video-studio/domain/episodes/index.ts`
and `video-studio/domain/index.ts` with the root set; the keys are `./episodes` and `.`, and
each entry's `source`/`types`/`import` targets still point at
`…/video-studio/domain/episodes/index.*`.

**Acceptance Scenarios**:

1. **Given** a root `VideoStudio.Domain` and a leaf barrel for `VideoStudio.Domain.Episodes`,
   **When** `package.json` is written, **Then** its exports key is `./episodes` and its targets
   are the unchanged full-namespace files.
2. **Given** the same root and a leaf barrel for exactly `VideoStudio.Domain`, **When**
   `package.json` is written, **Then** its key is `.` (the package root).
3. **Given** the same root and a leaf barrel for `Shared.Kernel` (outside the root), **When**
   `package.json` is written, **Then** its key keeps the full path `./shared/kernel`.
4. **Given** the same root and a leaf barrel for `VideoStudio.DomainEvents` (shares a string
   prefix but not a segment boundary), **When** `package.json` is written, **Then** its key
   keeps the full path `./video-studio/domain-events`.
5. **Given** no `SubpathRoot`, **When** `package.json` is written, **Then** the output is
   byte-identical to today.

### User Story 2 - Consumers import the short subpath (Priority: P1)

A consumer project references the producer assembly. Every cross-package import to a producer
type uses the producer's mapping, so the import specifier always matches an exported key.

**Independent Test**: `TranspileWithLibrary` with the producer root set; a consumer reference to
`VideoStudio.Domain.Episodes.Episode` emits `from "@video-studio/domain/episodes"`, and one to
`VideoStudio.Domain.Clock` emits `from "@video-studio/domain"`.

**Acceptance Scenarios**:

1. **Given** a producer with `SubpathRoot = "VideoStudio.Domain"`, **When** a consumer uses a type
   from `VideoStudio.Domain.Episodes`, **Then** the import is `"@video-studio/domain/episodes"`.
2. **Given** the same producer, **When** a consumer uses a type in exactly `VideoStudio.Domain`,
   **Then** the import is the bare package `"@video-studio/domain"`.
3. **Given** the same producer, **When** a consumer uses a type outside the root, **Then** the
   import keeps the full-namespace subpath.
4. The same mapping applies to `[NoContainer]` function imports from the producer.

### User Story 3 - Misconfiguration is reported, not silently miswired (Priority: P2)

**Acceptance Scenarios**:

1. **Given** `SubpathRoot = "Video Studio..Domain"` (not a dotted C# namespace), **When** the
   producer is transpiled for TypeScript, **Then** `MS0029` (error) is reported at the attribute
   and the producer build fails; a consumer of such a package treats it as having no root
   (full-path specifiers).
2. **Given** a root under which a stripped key equals another barrel's unstripped key (e.g. a
   root-namespace leaf barrel and the opt-in root aggregation barrel both claim `.`, or
   `VideoStudio.Domain.Shared` and a top-level `Shared` both claim `./shared`), **When** the
   producer is transformed, **Then** `MS0030` (error) names both barrels and the root, and the run
   stops before any file or cache entry is written — also under `--skip-package-json` and
   `--dry-run`.
3. **Given** `--namespace-barrels` and at least one type in exactly the root namespace, **When**
   the producer is transformed, **Then** `MS0030` reports the `.` collision between the root
   aggregation barrel and the root namespace's leaf barrel. Resolve it by dropping
   `--namespace-barrels`, narrowing `SubpathRoot` to a deeper namespace, or moving the root
   namespace's types into a child namespace.

### Edge Cases

- Root namespace has no emitted types and no sub-namespaces: nothing is stripped; no diagnostic.
- `--namespace-barrels` with types in exactly the root namespace: both barrels claim `.` →
  `MS0030` (see US3 scenario 3).
- `--src-root` output prefix (e.g. output in `src/domain`): the prefix is still prepended after
  stripping (`./domain/episodes`), exactly as today; the producer/consumer prefix gap that
  already exists for prefixed outputs is unchanged by this feature.
- Executable projects: no `exports` are written, so the root has no effect.
- Switching the root on/off: the previous run's transpiler-owned keys are pruned by the
  existing stale-entry rule (they point into `dist`), user-authored keys survive.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `EmitPackageAttribute` MUST expose an optional `SubpathRoot` (`string`, default
  `""`). It is declared per `[EmitPackage]` instance, so it applies only to that instance's
  `EmitTarget`. Empty means "not set" and preserves 005 behavior exactly.
- **FR-002**: A non-empty `SubpathRoot` MUST be a dotted C# namespace: one or more segments
  separated by `.`, each a valid C# identifier (no whitespace, no empty segment, no `global::`).
  Otherwise the TypeScript producer reports `MS0029` (`InvalidExportsSubpathRoot`, Error), which
  fails its build; every consumer treats the package as having no root. The value is validated
  once, into a core `PackageSubpathRoot` value object whose `None` state equals `default`.
- **FR-003**: Matching MUST be by whole namespace segment, performed on the kebab-cased path
  (the same kebab mapping 005 FR-003 uses for folders). A barrel whose namespace path equals
  the root's path maps to the package root; a barrel whose path starts with the root's path
  followed by `/` maps to the remainder; any other barrel keeps its full path.
  `VideoStudio.DomainEvents` is NOT under `VideoStudio.Domain`.
- **FR-004**: The stripping MUST apply to `package.json#exports` keys only. On-disk layout
  (005 FR-001–FR-004), the `imports` map (`#/*`, `#`, or a configured alias), internal imports
  (005 FR-016), and every entry's condition targets (`source`, `types`, `import`) MUST be
  unchanged.
- **FR-005**: A leaf barrel for exactly the root namespace MUST be exported as `.` (or as
  `./<prefix>` when an output prefix applies, matching how the root aggregation barrel is keyed
  today).
- **FR-006**: Cross-package resolution MUST read `SubpathRoot` from the referenced assembly's
  `[EmitPackage]` for the active target and apply FR-003 to the referenced type's namespace, so
  the consumer specifier is `<package>/<stripped-path>`, or the bare `<package>` for a type in
  exactly the root namespace. This covers type imports and `[NoContainer]` function imports.
- **FR-007**: When two barrels map to the same exports key, the TypeScript transform MUST report
  `MS0030` (`ExportsSubpathKeyCollision`, Error) naming both barrels and the declared root, so
  the run stops before files, caches or `package.json` are written. Should `package.json` still
  be written for the same file set, the barrel under the root keeps the key and the other is
  omitted from `exports`. Executables (no `exports`) are not checked.
- **FR-007a**: The per-group incremental cache MUST be invalidated when a referenced package's
  identity or `SubpathRoot` changes, so consumer files never keep stale cross-package
  specifiers.
- **FR-008**: The root MUST be declared only on the attribute. No MSBuild property or CLI flag
  sets it, because consumers resolve imports from the referenced assembly's metadata; a
  build-only setting would make producer exports and consumer imports diverge (see
  research.md R1).
- **FR-009**: The Dart target is out of scope; it ignores the root and never raises `MS0029`.
- **FR-010**: Tests MUST cover: stripped key, root-namespace key `.`, outside-root key,
  segment-boundary non-match, unchanged `imports`/targets, no-root regression, cross-package
  import with stripped and bare specifiers (types, enums, `[NoContainer]` functions, JSX
  components, merged import lines), invalid root diagnostic, collision diagnostic from the
  transform, Dart ignoring the root, and per-group cache invalidation.

### Amendments to 005

- **005 FR-002** — "with NO root/common-prefix stripping" continues to hold for the on-disk
  layout. The package-facing `exports` keys MAY be shortened by an explicitly declared
  `SubpathRoot` (this spec, FR-001–FR-007).
- **005 FR-013** — `package.json#exports` keys reflect the full-namespace layout of the leaf
  barrels, minus the declared `SubpathRoot` when one is set. `imports` entries are unchanged.

### Key Entities

- **Exports subpath root**: the declared namespace prefix, normalized to its kebab path
  (`VideoStudio.Domain` → `video-studio/domain`). Shared by the package writer and the
  cross-package import resolver so both sides agree.

## Success Criteria *(mandatory)*

- **SC-001**: For the issue's scenario, the consumer import is `@video-studio/domain/episodes`
  and the producer exports that key (0 occurrences of the repeated identity).
- **SC-002**: With no `SubpathRoot`, every existing golden file and package.json test passes
  unchanged.
- **SC-003**: On-disk generated paths are identical with and without the root.

## Assumptions

- The package identity is declared once on the attribute (ADR-0011) and the root belongs to
  that identity, so it is read from the same place.
- Choosing which barrel wins a collision is less important than surfacing it; the deterministic
  "under-the-root wins" rule keeps the common case (the short subpaths) stable.
