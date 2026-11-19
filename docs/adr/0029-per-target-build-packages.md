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
  part: user-input collection, toolchain keys, the `METANOBUILD002` check, the asset projection,
  the worker (tool probe and CLI `Exec`), the post-build commands and the aggregate
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
- **Hook order kept.** `MetanoTranspile` has no dependencies; its body runs
  `CallTarget Targets="_MetanoPrepare;<per-target targets>"`. A user `BeforeTargets="MetanoTranspile"`
  hook therefore runs before the settings are resolved and the assets projected, so a property it
  sets or a `MetanoAsset` it adds reaches the tool, and an `AfterTargets` hook runs after every
  target, as in 2.x. Each per-target target (`MetanoTranspileTs`) prepares in its body too, so it
  also works on its own. A hook that changes settings or assets attaches to `MetanoTranspile`:
  on `BeforeTargets="MetanoTranspileTs"` it applies only when that target runs on its own, because
  in a normal build the settings are already resolved when it fires.
- **Parent decides, worker executes.** Each per-target MSBuild target (`MetanoTranspileTs`) owns
  its incremental check (`Inputs` = user sources, assets, project file, its toolchain key;
  `Outputs` = its `.metano-stamp`). Only when out of date does it call the shared worker through
  the `MSBuild` task with the resolved, escaped values as global properties, so an up-to-date
  build spawns no worker and starts no tool. The whole argument line, assets included, is
  composed in the parent: the worker is a fresh evaluation and sees neither the items the parent
  produced by running targets (`@(ReferencePath)`) nor items or properties a user target added.
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

## Hardening follow-up (#706)

The effective CLI argument line is composed before the incremental check and hashed into
each target's toolchain key. Settings changed through `-p:` or `Directory.Build.*` therefore
rerun transpilation; an unchanged effective line leaves the key timestamp alone. `FilePrefix`
and `ExtraArgs` are MSBuild-escaped before argument-item creation: command-line quotes alone
do not prevent MSBuild from splitting literal semicolons.

Packages implementing the #706 common-file registration contract register their copy's path
and stable content hash. The first imported common file compares registered hashes against
its own and warns only on a demonstrated content mismatch; identical contents in different
package paths do not warn. A version-number difference by itself is not a warning.

**Mixed old/new limitation:** if a 3.0 package is next to a package predating this contract,
the old package does not register its common-file copy. With the new common file imported
first, only the new copy is known and matches, so there is no warning. With an old guarded
common file imported first, the old file has no check target and the new common file is
skipped, so there is again no warning. Skew in that two-package combination is undetectable;
absence of a warning does not prove compatibility. The 2.x `Metano.Build` package uses its
own legacy targets, not this shared-file contract, and is also not detected by this check.
Consumers should replace it with `Metano.Build.TypeScript` and align all per-target build
package versions. This change does not scan arbitrary imports, alter legacy targets, or
deprecate any package on nuget.org.

Argument composition is also a dependency of the TypeScript incremental target, so an
older shared file lacking the new pre-key composition hook cannot leave the worker's
arguments empty. If that older shared file wins the import guard, its old toolchain-key
implementation still does not hash the effective arguments; the new invalidation guarantee
requires the new common file. Mixed versions should therefore not be used even without a warning.

The packed-package smoke test now uses bounded transient-download retries, a reusable
nuget.org HTTP cache (separate from isolated package/tool caches), a transitive-only NuGet
consumer and a strict `-graphBuild -isolate` project-reference graph. Direct consumers also
check that changing `-p:`, `Directory.Build.props` or `Directory.Build.targets` reruns the
tool, preserves semicolons, and skips again with unchanged effective arguments.

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
