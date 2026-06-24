# 0025. Full-namespace output layout with self-cleaning incremental builds

- Status: accepted
- Date: 2026-06-23
- Deciders: Metano maintainers
- Spec: `specs/005-deterministic-output-layout/`

## Context and Problem Statement

The TypeScript target derived the output-tree root namespace as the *longest common prefix* of all transpilable types currently present (`NamespaceUtilities.FindCommonPrefix`, surfaced as `IrCompilation.LocalRootNamespace`, stripped by `PathNaming`). Two consequences made generated paths unstable and stale-prone:

1. The path of a type depended on *which other types existed*. With a single type the prefix collapsed to that type's entire namespace (everything stripped → flat at the output root); adding a sibling type in another namespace shrank the prefix and pushed the first type into a subfolder. So an unrelated edit silently relocated files.
2. Incremental builds only wrote the current output set — they never deleted files a prior run produced but the current run does not. Combined with (1), renames/moves left orphans that masked broken imports (observed in a real consumer: `import { UserProfile } from "../contracts"` resolved only through a stale orphan barrel).

## Decision Drivers

- Determinism: a type's path must be a pure function of its own namespace.
- No surprising relocation of unrelated files on incremental edits.
- Self-cleaning builds that don't require `--clean`.
- Tree-shakability of the generated package.
- Keep the change inside the TypeScript adapter; the core stays target-agnostic.

## Considered Options

- **A. Full namespace, no stripping** — a type's path is its complete kebab-cased namespace under the package/output root.
- **B. Strip a stable per-assembly root** (e.g. the declared `RootNamespace`) — shorter paths, but reintroduces a smaller variant of the same bug class (ambiguous/declared root, types outside it) and needs configuration.
- **C. Keep the longest-common-prefix** — rejected outright; it is the root cause.

## Decision

Adopt **Option A — full-namespace layout** for the TypeScript target, plus self-cleaning incremental builds:

- **D1 — Full namespace, nested folders.** No root stripping. `IrCompilation.LocalRootNamespace` is left empty for the TS target; `PathNaming` maps every namespace segment to a nested kebab-cased folder. `Vigiata.Contracts.Profiles.UserProfileDto` → `vigiata/contracts/profiles/user-profile.ts`. The package/output root is the assembly-equivalent container. Cross-package paths use the same rule (empty assembly root passed to `ComputeSubPath`), unifying local and cross-package layout. The package name already disambiguates across packages.
- **D2 — Barrels.** A per-namespace leaf barrel (`<full-ns>/index.ts`) is always generated — the external import unit (`<pkg-or-alias>/<full-namespace>`). The root namespace-aggregation barrel (`export namespace` tree) stays **opt-in** (`--namespace-barrels`) because it defeats tree-shaking; it is only an ergonomic single-entrypoint convenience.
- **D3 — Internal direct-file imports.** References between generated types resolve to the defining type's file (`#<alias>/<full-ns>/<file>` cross-namespace, `./<file>` same-namespace), never through a barrel. This keeps internal correctness independent of barrel generation/pruning and structurally prevents ESM import cycles.
- **Self-cleaning builds.** On a real (non-cache-hit) emit, the host reconciles the previous run's recorded output set (the cache `OutputHashes` manifest) against the current set and deletes the difference — restricted to Metano-generated paths — plus now-empty generated directories. Skipped on cache hit, `--no-cache`, and `--clean`; runs only after a successful write (failure-safe). A `layout=full-namespace-v1` token in the target `ConfigurationFingerprint` invalidates prior caches so the new layout is not masked by a stale hit on upgrade.

## Consequences

- **Positive**: paths are deterministic and stable across the type set; the "which root" question disappears; local and cross-package converge; incremental builds no longer accumulate orphans; the generated package stays tree-shakable; the change is confined to the TS adapter (the Dart target keeps its own `AssemblyRootNamespace`-based layout).
- **Negative**: paths/imports are more verbose — under a package named `web-server-contracts`, every path repeats `vigiata/contracts/...` (the same redundancy .NET has between an assembly and its namespaces). The opt-in root aggregation barrel mitigates this ergonomically.
- **Churn**: existing samples and golden fixtures that declare a namespace gain the full-namespace prefix. This is intentional (regenerate and review), not a regression; global-namespace sources are unaffected.

## Relationship to other ADRs

- Supersedes the longest-common-prefix layout behavior.
- Adjusts **ADR-0006 (namespace-first barrel imports)**: barrels remain the *external* import contract, but *internal* generated-to-generated references now resolve to files directly (D3).
- Builds on **ADR-0021 (incremental cache)**: the cache `OutputHashes` becomes the manifest that drives orphan reconciliation.
- Composes with the configurable import-alias feature (spec 004): the alias still maps to the output root; subpaths under it are now full-namespace.
