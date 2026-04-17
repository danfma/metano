# ADR-0004 — Cross-project references via Roslyn compilation references

**Status:** Accepted
**Date:** 2026-03-05

## Context

Consumers of Metano want to reference types from sibling C# projects and
have them automatically resolve to npm package imports. A `Tracker` class
in the consumer project that has a `AcmeIssues.Issue?` field must emit
`import type { Issue } from "@acme/issues";` in the generated TypeScript,
and the consumer's `package.json#dependencies` must list `@acme/issues`
with the right version.

Two ways to pull the required cross-assembly metadata were on the table:

1. **Custom `.metalib` sidecar** — a binary metadata file embedded in each
   NuGet package, containing type signatures + namespace → package
   mapping + guard metadata. Read by the consumer's compiler when
   resolving cross-assembly references.
2. **Roslyn compilation references** — reuse the in-process
   `CSharpCompilation` that already loads referenced assemblies'
   metadata when the user does `<ProjectReference>` or `<PackageReference>`
   in their `.csproj`.

The `.metalib` route is the long-term answer for libraries shipped as
NuGet packages with no source. But designing, generating, and embedding
`.metalib` would block the `ProjectReference` use case indefinitely — and
the monorepo / Bun workspace flow that powers the samples is entirely
`ProjectReference`.

## Decision

For the source-available path (`ProjectReference` within the same solution
or Bun workspace), reuse Roslyn's compilation references as the primary
cross-assembly channel. `TypeTransformer.DiscoverCrossAssemblyTypes` walks
`compilation.References`, loads each referenced assembly's public types,
and registers them into `_crossAssemblyTypeMap` keyed by **symbol
identity** (not string name) so two libraries with overlapping namespaces
don't collide.

Cross-package routing requires the referenced assembly to declare:

- `[assembly: TranspileAssembly]` — opt-in for transpilation.
- `[assembly: EmitPackage("@scope/name", target = EmitTarget.JavaScript)]`
  — npm package identity. Multiple `EmitPackage` instances are allowed,
  one per target.

Types from such assemblies carry a `TsNamedType.Origin` of
`TsTypeOrigin(PackageName, SubPath, IsDefault)`, populated by `TypeMapper`
at construction time. The `ImportCollector` consumes the origin directly
during its AST walk — no name-based re-lookup, no string matching — and
emits `import { Foo } from "<package>/<sub-path>"` computed against the
**library's own** root namespace (so namespace paths stay consistent
regardless of where the consumer sits).

When a referenced assembly has `[TranspileAssembly]` but no
`[EmitPackage(JavaScript)]`, the compiler emits **MS0007** as a hard error
at the consumer site (deduplicated by type display name so one missing
attribute produces exactly one error per unique type). Silently skipping
would leave dangling identifiers in the generated `.ts` file and trigger
confusing `tsgo` errors downstream.

Cross-package `[Import]` declarations in referenced assemblies fold into
the consumer's local `_externalImportMap`, so transitive external bindings
(`decimal.js`, `hono`, polyfills) work without the consumer re-declaring
them.

The `.metalib` path is deferred to a follow-up, tracked as
[issue #27](https://github.com/danfma/metano/issues/27). When added, it
will feed into the same `_crossAssemblyTypeMap` — additive, not a rewrite.

## Consequences

- (+) Zero invention. Roslyn already resolves `ProjectReference`s and
  loads their metadata; we just consume what's there.
- (+) Symbol-identity keying prevents namespace-collision bugs. Two
  assemblies each declaring `Money` under different namespaces produce
  two distinct entries in the map, and the consumer resolves each
  independently.
- (+) `TsTypeOrigin` moves cross-package resolution from the import
  collector (a string-lookup consumer) to the type mapper (a symbol-aware
  producer). The import collector becomes a dumb AST walker that reads
  the origin off the type it visits.
- (+) Auto-deps merge into the consumer's `package.json#dependencies`
  with versions derived from `IAssemblySymbol.Identity.Version` (`^Major.Minor.Patch`),
  falling back to `workspace:*` for unversioned sibling projects.
  See [ADR-0011](0011-emit-package-ssot.md) — planned.
- (+) When the `.metalib` path is added later, it will plug into the same
  `_crossAssemblyTypeMap` without touching the import collector or the
  type mapper.
- (−) NuGet-shipped libraries are not supported yet — they need the
  `.metalib` follow-up. Users who want to distribute a Metano-annotated
  library as a NuGet package must wait or ship it as source within a
  monorepo.
- (−) Consumers must decorate referenced assemblies with `[EmitPackage]`.
  Missing decoration fails the consumer build with MS0007 — strict but
  unambiguous.

## Alternatives considered

- **Custom IR + `.metalib` as the primary channel**: rejected for now.
  Would duplicate Roslyn's metadata loader and block the `ProjectReference`
  flow while the format was being designed. Still needed for the NuGet
  path later, but additive.
- **String-based name matching across assemblies**: rejected. Breaks on
  namespace collisions and provides no symbol-level disambiguation.
- **Source generators that emit a TS-description file at C# compile
  time**: rejected. Adds build-step complexity and forces a specific
  build pipeline on every consuming project.

## References

- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs` —
  `DiscoverCrossAssemblyTypes`
- `src/Metano.Compiler.TypeScript/Transformation/IrTypeOriginResolverFactory.cs` —
  populates `IrTypeOrigin` on every `IrNamedTypeRef` during extraction
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsTypeMapper.cs` — propagates
  `IrTypeOrigin` onto `TsNamedType.Origin` during lowering
- `src/Metano.Compiler.TypeScript/TypeScript/AST/TsTypeOrigin.cs`
- `src/Metano/Annotations/TranspileAssemblyAttribute.cs`
- `src/Metano/Annotations/EmitPackageAttribute.cs`
- `tests/Metano.Tests/CrossPackageImportTests.cs`
- Related: [ADR-0011](0011-emit-package-ssot.md) (`[EmitPackage]` as
  package.json SSoT + auto-deps merge — planned)

## Post-refactor note (2026-04)

Roslyn is still the front-end (and still carries the cross-assembly
metadata that makes this decision cheap), but the flow now goes through
the shared IR. The Roslyn `Compilation` is walked by extractors in
`src/Metano.Compiler/Extraction/` which stamp `IrTypeOrigin(PackageId,
Namespace, VersionHint?)` on every `IrNamedTypeRef`; the target bridges
read it without touching Roslyn symbols. `TypeMapper` is retired
(replaced by `IrToTsTypeMapper`), and the alternative "Custom IR + ..."
path listed above has partially materialized: the IR is the canonical
representation, Roslyn is just the loader. See
[ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md). A
follow-up will physically separate the C# frontend so `Metano.Compiler`
stops referencing `Microsoft.CodeAnalysis.*`, opening the door to
non-Roslyn front-ends.
