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

**Decision**: `MS0029` Error; the barrel under the root keeps the key; the CLI exits non-zero.

**Rationale**: A collision means some consumer import would resolve to the wrong barrel. A
warning would let that ship. The writer still writes a deterministic file so the regeneration
is inspectable.

## R5 — Diagnostic code

**Decision**: New `MS0029 InvalidExportsSubpathRoot`, covering both malformed values and
key collisions (same pattern as `MS0027`/`MS0028`, one code per attribute misuse). `MS0007`
stays reserved for package identity problems.
