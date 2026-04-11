# ADR-0007 — Output conventions: kebab-case, leaf-only barrels, `#/` alias, `sideEffects: false`

**Status:** Accepted
**Date:** 2026-03-01

## Context

Several small decisions about the shape of the generated package had to be
made together because they interact. Each in isolation is minor, but the
combination defines what idiomatic Metano output looks like to a consumer:

- File naming — PascalCase matches the C# source; kebab-case matches
  Node / npm conventions.
- Directory structure — flat emit or one folder per namespace.
- How consumers reference generated files — absolute relative paths, a
  path alias, or subpath exports.
- Whether the emitted package is tree-shakeable by default.

Without pinning these down together, the output would drift: a commit
fixing one surface (e.g. making imports tree-shakeable) would fight the
assumptions of another (e.g. the barrel strategy), and consumers would
see their import paths churn between releases.

## Decision

Five conventions, all load-bearing together:

1. **Kebab-case file names.** `UserId.cs` → `user-id.ts`.
   `SymbolHelper.ToKebabCase()` is the centralized helper and is used
   everywhere filenames and import paths are computed.

2. **One type = one file.** Each transpilable C# type produces exactly
   one `.ts` file named after the type, placed under a directory
   mirroring the namespace path (stripping the discovered root
   namespace). The `[EmitInFile("name")]` escape hatch allows
   co-locating multiple types in one file when the C# source already
   models them as a group.

3. **`#/` subpath alias for intra-package imports.** Consumers write
   `import { X } from "#/issues/domain";` instead of fragile
   `../../issues/domain`. Implemented via `package.json#imports`
   conditional exports — dist (`.js` + `.d.ts`) is preferred, source
   `.ts` is the fallback for dev.

4. **Leaf-only barrels.** Each directory that contains generated type
   files gets its own `index.ts` re-exporting the types in that
   directory. Parent directories do **not** re-export child
   directories — there is no `export * from "./issues/domain"` at the
   package root. When a user-defined type would produce a file named
   `index.ts` (e.g. a type called `Index`), the barrel is suppressed
   for that directory to avoid the collision.

5. **`"sideEffects": false`** in the generated `package.json`. Tells
   every bundler (Bun, tsgo, esbuild, Rollup, Vite, webpack) that no
   module in the package has import-time side effects, unlocking
   aggressive dead-code elimination.

The five are load-bearing together: removing leaf-only barrels (4) makes
`sideEffects: false` (5) untrustworthy because parent `export *`
aggregations defeat tree-shaking in practice. Removing the `#/` alias
(3) forces relative paths that break when files move between namespaces.
Dropping kebab-case (1) makes the output look foreign next to the rest
of the Node/npm ecosystem. They were made as part of the same design
conversation and should stay linked in the ADR log.

## Consequences

- (+) Consumers import from predictable paths. `#/issues/domain` survives
  any reorganization inside the namespace — consumer imports do not
  churn.
- (+) Emitted packages are tree-shakeable out of the box. Bundlers drop
  unused types without extra config from the consumer.
- (+) Generated files look native to Node/Bun. Kebab-case filenames
  match what the vast majority of npm / JSR packages ship.
- (+) `index.ts` collision detection prevents one pathological case
  (`[Transpile] class Index`) from silently corrupting the barrel.
- (+) Conditional exports in `package.json#imports` give dev mode a
  source-TS fallback while production resolves `.d.ts` + `.js` from
  `dist/`, so a single compile produces a package that works in both
  workflows.
- (−) Root namespace of a package that only has sub-namespaces (no types
  directly at the root) gets no root barrel, so `import { X } from "@scope/pkg"`
  without a sub-path doesn't resolve. Tracked as an opt-in follow-up
  (see [ADR-0006](0006-namespace-first-barrel-imports.md) — the
  `--namespace-barrels` flag).
- (−) Users with a type named exactly `Index` lose the barrel for that
  directory. Rare but possible; the collision is logged so the user
  understands what happened.

## Alternatives considered

- **PascalCase filenames** (`UserId.ts`): rejected. Looks foreign in
  Node/Bun consumer contexts where everyone else ships kebab-case.
- **Relative paths only**, no `#/` alias: rejected. Long relative paths
  in deeply nested folders are brittle under refactors and hurt
  readability.
- **Aggregated root barrel by default** (`export * from "./issues/domain"; ...`):
  rejected. Breaks tree-shaking in practice. See
  [ADR-0006](0006-namespace-first-barrel-imports.md) for the full
  argument.
- **Omit `sideEffects` from the generated `package.json`**: rejected.
  Bundlers default to assuming side-effects exist; omission silently
  disables tree-shaking in the consumer's bundle.

## References

- `src/Metano.Compiler/SymbolHelper.cs` — `ToKebabCase`
- `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`
- `src/Metano.Compiler.TypeScript/Transformation/BarrelFileGenerator.cs`
  — leaf-only + collision detection
- `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs` — `imports`,
  `exports`, `sideEffects`, `type` fields
- `tests/Metano.Tests/NamespaceTranspileTests.cs`,
  `EmitPackageTests.cs`
- Related: [ADR-0006](0006-namespace-first-barrel-imports.md)
  (namespace-first imports builds on these conventions).
