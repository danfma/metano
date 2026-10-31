# Research: Opt-in exports subpath root

## R1 — Attribute only, no MSBuild property

**Decision**: `SubpathRoot` lives on `[assembly: EmitPackage]`; no `MetanoExportsSubpathRoot`
MSBuild property and no CLI flag.

**Rationale**: A consumer resolves a cross-package import from the *referenced assembly's*
attributes (`SymbolHelper.GetEmitPackageInfo`), never from the producer's MSBuild properties. A
build-only setting would change the producer's `exports` keys while consumers kept emitting the
full path, which then no longer exists in `exports` — a silent break. Making the property inject
an assembly attribute is not viable either: `[EmitPackage]` requires the package name, allows
multiple instances (first matching target wins), and a second synthesized instance would either
duplicate or conflict with the hand-written one.

**Alternatives rejected**: property-only (breaks consumers); property that synthesizes the
attribute (duplicate-identity ambiguity, hidden source of truth).

## R2 — Matching on the kebab path

**Decision**: Normalize the root to its kebab path and strip barrel directory paths on whole
`/` segments.

**Rationale**: The package writer only sees generated file names (kebab paths); the
cross-package resolver turns namespaces into kebab paths (`PathNaming.ComputeExportedSubPath`,
which replaces the now-unused `ComputeSubPath`).
Applying one function to the kebab path on both sides guarantees they agree, including in the
pathological case where two distinct namespaces kebab to the same folder.

## R3 — Root-namespace barrel claims `.`

**Decision**: A leaf barrel for exactly the root namespace is exported as `.`; the consumer
imports the bare package name.

**Rationale**: `ImportCollector` already emits the bare package name for an empty cross-package
subpath, so mapping the root namespace to `""` needs no import-side change.
`.` is where the opt-in root aggregation barrel sits today; when both exist they collide and
FR-007 reports it rather than picking a surprising winner silently.

## R4 — Collisions are errors

**Decision**: `MS0030` Error raised by the TypeScript transform (not by the `package.json`
writer); the barrel under the root keeps the key.

**Rationale**: A collision means some consumer import would resolve to the wrong barrel. A
warning would let that ship. Raising it in the transform puts it in the host's error count,
which stops the run before generated files and caches are written; a writer-time error would
come after both, be skipped by `--skip-package-json` / `--dry-run`, and be hidden by the next
cache hit.

## R5 — Diagnostic code

**Decision**: Two codes: `MS0029 InvalidExportsSubpathRoot` (malformed value) and
`MS0030 ExportsSubpathKeyCollision` (two barrels claim one key). They have different causes and
fixes, and a collision can involve the root aggregation barrel, which is not stripped at all.
`MS0007` stays reserved for package identity problems.

## R6 — Per-group cache key

**Decision**: The per-group cache fingerprint includes every referenced package's name and
`SubpathRoot`.

**Rationale**: Group closure hashes cover only the current assembly's types. Without the extra
key, a producer that changes its root would leave consumer groups reused with the old
specifiers. (The host-level full cache already keys on reference fingerprints.)

## R7 — Value object in the core

**Decision**: `Metano.Compiler.PackageSubpathRoot`, a `readonly record struct` validated once by
`TryFrom`, with `None == default`. It flows through `EmitPackageInfo`, `IrCompilation` and
`IrTypeOrigin`; the TypeScript target only owns the namespace-to-subpath mapping
(`PackageExportSubpaths`).
