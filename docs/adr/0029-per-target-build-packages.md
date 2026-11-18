# ADR-0029 — Per-target MSBuild packages with a shared worker

**Status:** Accepted — implemented by #328 (baseline FR-085 to FR-087), ships in 3.0.0
**Date:** 2026-11-18

## Context

The language targets are isolated at the compiler level: `Metano.Compiler.TypeScript`
(`metano-typescript`) and `Metano.Compiler.Dart` (`metano-dart`) are separate tools over the
shared core (ADR-0001). The MSBuild glue is not. `Metano.Build` has a language-neutral name but is
TypeScript-only: `Metano.Build.targets` hard-codes `dotnet metano-typescript`, its description
says "to TypeScript", `METANOBUILD001` names the TypeScript tool, and most of its properties
(`MetanoDist`, `MetanoImportAlias`, `MetanoSkipPackageJson`, ...) are npm concepts the Dart CLI
does not accept. Dart has no build integration at all.

Three MSBuild facts constrain the design, each checked on this repository's SDK:

- A property name cannot be built from parts: `$(Metano$(Lang)OutputDir)` fails with `MSB4184`,
  so one shared target cannot be generic over a setting prefix.
- A target runs at most once per project instance, so one shared target cannot run twice in the
  same instance with different settings. The `MSBuild` task on the same project with different
  global properties creates a separate instance, where the shared target runs once per settings
  set; values carrying `;` pass through `$([MSBuild]::Escape(...))`.
- That separate instance does not see items the parent produced by running targets: its
  `@(ReferencePath)` is empty (167 items in the parent, 0 in the worker in the check). Inputs
  that depend on resolved references are computed in the parent and passed as properties.

Two more behaviours of 2.x are kept on purpose: a target listed in `AfterTargets` runs even when
the target it follows was skipped (so `MetanoPostBuild` runs on every build), and NuGet package
targets are imported before `Directory.Build.targets`, so 2.x's evaluation-time
`MetanoEnabled` default ignores a `MetanoOutputDir` set there.

## Decision

- **One package per target.** `Metano.Build.TypeScript` replaces `Metano.Build`; a future
  `Metano.Build.Dart` is one `.targets` file plus one `.csproj`. `Metano.Build` gets no 3.x
  version (no stub, no metapackage); it is deprecated on nuget.org after the release.
- **One shared source file, linked.** `Metano.Build.Common.targets` holds the target-independent
  part: user-input collection, toolchain keys, the `METANOBUILD002` check, the worker (tool
  probe, asset projection, CLI `Exec`), the post-build commands and the aggregate
  `MetanoTranspile` / `MetanoPostBuild` targets. Both `.targets` files live in one source folder,
  `src/Metano.Build.Targets/`; each package project (`src/Metano.Build.TypeScript/`) packs its
  per-target file and the shared one side by side in `build/` and `buildTransitive/`, so the
  per-target file imports the shared one from its own directory in the repository and in the
  package alike. An import guard loads it once when a project references two build packages. No
  third NuGet package.
- **Registration contract.** The shared file never reads a prefixed name. Each per-target
  resolve target registers one `_MetanoEnabledTarget` item per enabled target (language tag,
  `OutputDir`, `OutputDirFull`, `ToolchainKeyFile`, `CompilerProject`, `ToolStoreName`,
  `PostBuildCommand`) and appends itself and its transpile target to
  `_MetanoResolveTargetsDependsOn` / `_MetanoTranspileTargets`; the shared file batches over the
  items (toolchain keys, `METANOBUILD002`, post-build).
- **One resolution rule.** Each per-target file spells out one line per setting,
  `$([MSBuild]::ValueOrDefault('$(MetanoTs<S>)', '$(Metano<S>)'))` then the target default,
  inside a resolve target (not at evaluation, so `Directory.Build.targets` values count). The
  unprefixed names stay the global settings, so a single-target project migrates by swapping the
  `PackageReference`.
- **Hook order kept.** `MetanoTranspile` prepares every target in its dependencies and runs the
  per-target transpile targets from its body (`CallTarget`; everything they read was produced by
  the dependencies), so a user `BeforeTargets="MetanoTranspile"` hook still runs before any tool
  and an `AfterTargets` hook after every target, as in 2.x.
- **Parent decides, worker executes.** Each per-target MSBuild target (`MetanoTranspileTs`) owns
  its incremental check (`Inputs` = user sources, assets, project file, its toolchain key;
  `Outputs` = its `.metano-stamp`). Only when out of date does it call the shared worker through
  the `MSBuild` task with the resolved, escaped values as global properties, so an up-to-date
  build spawns no worker and starts no tool. Values that need `@(ReferencePath)` are resolved in
  the parent.
- **Collisions are errors.** Two enabled targets resolving to the same output directory fail
  with `METANOBUILD002` before any tool runs.
- **Tools stay separate.** A NuGet package cannot install a .NET tool, so the build package
  does not try; a committed `.config/dotnet-tools.json` plus `dotnet tool restore` is the
  documented way to install several tools, and `METANOBUILD001` names the missing one.

## Consequences

- (+) Each target owns its properties, defaults, tool and error text; the Dart integration
  becomes additive (one file, one project), in line with ADR-0001.
- (+) The shared file has one source, so the per-target packages cannot drift (the #230 failure
  mode).
- (+) Several targets per project with per-target overrides, and settings in
  `Directory.Build.targets` now apply.
- (−) Breaking change: every consumer swaps the package id. Nothing forces it: a floating `2.*`
  reference keeps restoring `Metano.Build` 2.x, and only the deprecation notice and the README
  tell users to move.
- (−) An out-of-date target pays one extra project evaluation (the worker instance).
- (−) The override rule is new public surface; the settings list in FR-086 is the contract and a
  test pins it to the `.targets` file.
- (−) An empty per-target value cannot clear a global one (MSBuild treats empty as unset).
- (−) A global `MetanoCompilerProject`, `MetanoExtraArgs` or `MetanoPostBuildCommand` reaches
  every enabled target; the docs recommend the prefixed form with more than one target.
- (−) A disabled target no longer runs its post-build command (2.x ran it even with
  `MetanoEnabled=false`); listed in the 3.0 migration notes.

## Alternatives considered

- **A third `Metano.Build.Common` NuGet package**: one more id to publish and version for a file
  that is never used alone; linking the source gives the same single source.
- **A `Metano.Build` 3.x stub that fails the build, or a metapackage of every target**: rejected
  by the maintainer (#328 decision 4); revisit only if the deprecation notice proves too weak.
- **Target batching over one item per target instead of worker instances**: one instance, but
  per-batch `Inputs`/`Outputs`, per-target asset filtering and conditional flags all become
  metadata expressions; the worker instance keeps the 2.x target body almost verbatim.
- **Embedding the compiler in the build package**: removes `METANOBUILD001` but makes the
  package large and couples it to one target's tool; a separate decision.
- **A single umbrella tool**: not needed while a tool manifest installs several tools.

## References

- `src/Metano.Build.Targets/Metano.Build.Common.targets`, `src/Metano.Build.Targets/Metano.Build.TypeScript.targets`,
  `src/Metano.Build.TypeScript/Metano.Build.TypeScript.csproj` (2.x: `src/Metano.Build/build/Metano.Build.targets`)
- Tests: `tests/Metano.Tests/Build/` (synthetic second target, settings contract, toolchain keys);
  `scripts/smoke-test-build-package.sh` (packed package, CI and release gate)
- [ADR-0001](0001-target-agnostic-core.md), [ADR-0021](0021-incremental-cache.md)
- Issues #328 (this decision), #230 (single canonical `.targets`), #241 (missing-tool detection),
  #28 (Dart target), #32 (tooling and packaging umbrella)
- Spec: `specs/001-project-baseline-evolution/spec.md` FR-085 to FR-087; spec 011 D16 (one 3.0.0)
