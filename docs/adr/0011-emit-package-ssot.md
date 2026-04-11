# ADR-0011 — `[EmitPackage]` as single source of truth for `package.json#name`

**Status:** Accepted
**Date:** 2026-03-20

## Context

Cross-package import resolution ([ADR-0004](0004-cross-project-references-via-roslyn.md))
depends on the consumer importing from the **exact same** package name
that the library emitted when it transpiled its types. Drift between
the library's `package.json#name` and the name embedded in the
generated imports produces imports that don't resolve, and the failure
mode is obscure from the consumer's point of view: "`@acme/lib/domain`
doesn't exist" — but it does, just under a different name.

Three places can claim to own a package name:

1. A hand-written `package.json#name` field at the library root.
2. The `[assembly: EmitPackage("name", target)]` attribute in the C#
   source.
3. The directory name of the library project on disk.

Without an explicit rule, whichever source wins is accidental. Worse,
a refactor that renames one but not the other (say, renaming the
directory but forgetting the attribute) silently breaks every consumer
on the next compile.

The same problem extends to cross-package dependencies: when the
library is a sibling project in a Bun monorepo, the consumer's
`package.json#dependencies` must list it with a matching version
(`workspace:*` or `^1.2.3`). Manual maintenance drifts.

## Decision

The `[assembly: EmitPackage(name, target)]` attribute is the **single
source of truth** for `package.json#name` in every target-emitted
package. The compiler's `PackageJsonWriter` runs a non-destructive
merge that behaves as follows:

1. Read the attribute from the library assembly.
2. Open the consumer's existing `package.json` if one exists;
   otherwise start from a minimal skeleton.
3. If the existing `package.json#name` diverges from the attribute
   value, emit a **warning** (`MS0007`, see
   [ADR-0010](0010-metano-diagnostics.md)) explaining the mismatch,
   then **overwrite** the name with the attribute value. The build
   continues. The attribute wins because cross-package imports
   produced by the compiler are computed against it — if the file and
   the attribute disagree, consumers will follow the attribute, so
   the file must follow the attribute too.
4. Every other hand-written field (`description`, `scripts`,
   `devDependencies`, `bin`, `files`, `keywords`, …) is preserved
   untouched.
5. Controlled fields are overwritten on every compile: `type`,
   `sideEffects`, `imports`, `exports`, and the merged subset of
   `dependencies` described below.

Cross-package dependencies are **auto-merged** into
`package.json#dependencies`. Three contribution paths feed the merge:

- **Cross-assembly types via `[EmitPackage]`.** Version comes from
  `IAssemblySymbol.Identity.Version`, formatted as `^Major.Minor.Patch`.
  Unversioned sibling projects in a Bun monorepo (Roslyn defaults the
  version to `0.0.0.0`) fall back to `workspace:*`.
- **External types via `[Import("name", from: "pkg", Version = "^x.y.z")]`.**
  Version comes from the attribute. Without `Version`, the type still
  imports correctly but no auto-dep entry is created — the user adds
  the dependency manually.
- **BCL types via `[ExportFromBcl(..., Version = "^x.y.z")]`.** Same
  model as `[Import]`. The default `decimal` mapping in
  `Metano/Runtime/Decimal.cs` ships with `Version = "^10.6.0"`, so
  any consumer that uses C# `decimal` gets `decimal.js` listed
  automatically.

The merge rule is asymmetric:

- Keys the compiler tracked are **always overwritten** with the
  compiler-computed version. The C# project is the source of truth
  for the cross-assembly version; hand-editing the dependency would
  drift again on the next compile.
- Keys the compiler did **not** track are **left completely alone**.
  A hand-written `react: "^18.0.0"` or `bun-types: "latest"` survives
  regeneration untouched.

The `[EmitPackage]` attribute is `[AttributeUsage(AllowMultiple = true)]`
and takes an `EmitTarget` enum — the same assembly can declare
`[EmitPackage("@acme/lib", EmitTarget.JavaScript)]` and (future)
`[EmitPackage("com.acme.lib", EmitTarget.Kotlin)]` side by side,
each resolved by its respective target compiler.

## Consequences

- (+) Cross-package imports always resolve. The producer's emitted
  `package.json#name` matches what the consumer's imports reference,
  enforced at build time.
- (+) Renaming a package is a single-attribute change. The next
  compilation warns via MS0007 and rewrites the `package.json`
  accordingly; consumers recompile and their imports track the new
  name.
- (+) Hand-written `package.json` content survives. Users can add
  scripts, devDependencies, runtime packages outside the compiler's
  vocabulary, and arbitrary metadata without fear of losing it on
  regenerate.
- (+) Auto-deps stay in sync with the library's `AssemblyVersion`.
  Bumping the library version in the C# project propagates to every
  consumer's `package.json#dependencies` on the next compile.
- (+) Multi-target emission is a natural extension. A Dart or Kotlin
  target adds a second `[EmitPackage]` instance; the JS writer
  ignores the non-JS one, and the future Dart writer ignores the
  non-Dart ones.
- (+) The MS0007 warning when names diverge is explicit. Silent
  overwrite would be worse than today's noisy-but-clear behavior.
- (−) The attribute is required for cross-package use. A library
  without it can't be consumed across packages; the consumer sees an
  MS0007 error at build time. Strict but unambiguous.
- (−) Users who need to pin a specific cross-package dependency
  version differently from the library's C# version have no clean
  escape hatch — they can hand-edit `package.json#dependencies`, but
  the next compile will overwrite their change. Rare case; not worth
  an opt-out today.

## Alternatives considered

- **Trust whatever's in `package.json#name`**: rejected. Silent drift,
  no enforcement.
- **Use the project directory name**: rejected. Couples the package
  identity to the filesystem; moving or renaming the directory would
  silently rename the package.
- **Require both to match and refuse to build on mismatch**: rejected.
  Strict but user-hostile — the writer already knows the right answer,
  so warning-and-overwrite gives the user feedback without blocking
  the build.
- **Let users override auto-deps with a manual pin in
  `dependencies`**: rejected for now. Would require a mechanism to
  remember "the user set this explicitly, don't touch it again",
  which is complex and not needed for any current sample.

## References

- `src/Metano/Annotations/EmitPackageAttribute.cs`
- `src/Metano/Annotations/EmitTargetEnum.cs`
- `src/Metano/Annotations/ImportAttribute.cs` (the `Version` field)
- `src/Metano/Annotations/ExportFromBclAttribute.cs` (the `Version` field)
- `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs` — the
  non-destructive merge
- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`
  — `CrossPackageDependencies` computation
- `tests/Metano.Tests/EmitPackageTests.cs`
- `tests/Metano.Tests/CrossPackageImportTests.cs` —
  `UsedCrossPackages_TrackedForAutoDependencies`,
  `UnversionedLibrary_GetsWorkspaceDepSpec`
- Related: [ADR-0004](0004-cross-project-references-via-roslyn.md)
  (how cross-assembly types are discovered),
  [ADR-0010](0010-metano-diagnostics.md) (MS0007 is how drift is
  reported).
