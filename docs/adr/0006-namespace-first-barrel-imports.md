# ADR-0006 — Namespace-first barrel imports + same-namespace relative

**Status:** Accepted
**Date:** 2026-04-11

## Context

C# code is organized around namespaces: `using Acme.Issues.Domain;` pulls
every type from a namespace with a single line. TypeScript has no
namespace-level import — every import is module-scoped. A naive one
file per type emission forces consumers to import every type by its
individual file path, producing output like:

```ts
import { Issue } from "#/issues/domain/issue";
import { IssueId } from "#/issues/domain/issue-id";
import { IssuePriority } from "#/issues/domain/issue-priority";
import { IssueStatus } from "#/issues/domain/issue-status";
import { IssueType } from "#/issues/domain/issue-type";
```

That shape has three problems. It explodes the number of import lines in
the generated code; it leaks the file organization (an implementation
detail) into the consumer's import surface, so renaming or moving a file
breaks consumer imports; and it diverges from the C# mental model, where
the consumer thinks about the namespace, not the file tree.

## Decision

Resolve imports **barrel-first**, with a narrow escape hatch for
same-namespace references. The rules, in priority order:

| Case                                          | Import form                                                |
| --------------------------------------------- | ---------------------------------------------------------- |
| Same namespace as importer                    | Relative file import: `import { X } from "./x";`           |
| Different namespace, same package             | Namespace barrel: `import { X } from "#/issues/domain";`   |
| Root namespace of the package                 | Root barrel: `import { X } from "#";`                      |
| Cross-package, different namespace            | `import { X } from "@scope/lib/domain";`                   |
| Cross-package, root namespace match           | `import { X } from "@scope/lib";`                          |

Same-namespace deliberately falls back to a relative file import instead
of going through the namespace barrel. Routing every same-namespace
reference through the barrel would introduce the trivial cycle
`file.ts → own namespace barrel → own file.ts`, which TS can handle but
bundlers flag as circular and readers find confusing. The relative
`./sibling` form is both shorter and cycle-free.

Leaf-only barrels: `BarrelFileGenerator` emits exactly one `index.ts` per
directory that contains generated type files. Sub-directories are **not**
re-exported by parent barrels. Consumers go through the specific
namespace's barrel — there is no `export * from "./issues/domain"` in the
package root. Without this rule, tree-shaking under current toolchains
(Bun, tsgo, esbuild, Vite) stops working: an `export *` aggregation
defeats the dead-code-elimination heuristics the bundlers rely on to
drop unused modules.

**Merged imports with a 3-case type-only form.** Multiple names from the
same barrel collapse into a single `TsImport` line in both the
cross-package and the local branches of `ImportCollector`. The emit
chooses one of three shapes per barrel:

- All names value → `import { A, B } from "...";`
- All names type-only → `import type { A, B } from "...";` (whole statement)
- Mixed → `import { V1, V2, type T1, type T2 } from "...";` (values first)

The whole-statement form in the all-type-only case is preferred over the
per-name form because Biome's `noImportTypeQualifier` lint flags the
per-name form when no values are present. The values-first ordering in
the mixed form is chosen for readability — `{ Value, type TypeOnly }`
reads more naturally than interleaving by ordinal name.

**Root-barrel aggregation deliberately left out of the default.** When a
package's root namespace is purely sub-namespaces (e.g.
`SampleIssueTracker.Issues.*`, `SampleIssueTracker.Planning.*`,
`SampleIssueTracker.SharedKernel.*`, with no types at the bare root), no
`src/index.ts` is emitted, and `import { X } from "@acme/sample-issue-tracker"`
does not resolve. The naive fix — aggregating sub-barrels into a root
barrel via `export * from "./issues/domain"; export * from "./planning/domain"; …`
— defeats tree-shaking as described above. An alternative shape using
`export namespace` blocks to mirror the C# hierarchy preserves
tree-shaking in principle but changes every consumer's import surface.
Neither is acceptable as the default, so the feature is tracked as an
**opt-in flag** (`--namespace-barrels`) in the pending-work backlog.

`CyclicReferenceDetector` normalizes `#`, `#/...`, and `./...` in its
graph, so cycles across any of the three forms surface as MS0005
diagnostics before tsgo chokes on them.

## Consequences

- (+) Generated output matches the C# mental model. Consumers import by
  namespace (`from "#/issues/domain"`), not by file path.
- (+) Stable import surface. Renaming or moving an internal file inside
  a namespace doesn't break consumer imports; the barrel absorbs the
  change.
- (+) Dramatically smaller import blocks. `sample-issue-tracker`'s
  `issue-service.ts` dropped from 9 import lines to 2 after merging was
  introduced (issue #12).
- (+) Tree-shaking preserved. Leaf-only barrels mean any `export *`
  boundary is local to a single namespace, not aggregated to the root.
- (+) The 3-case type-only form is both correct and lint-friendly under
  `verbatimModuleSyntax` + Biome.
- (−) Root-barrel convenience (`from "@scope/pkg"`) only works when the
  package has types at the bare root namespace. Packages whose root is
  purely sub-namespaces force consumers to go through a specific
  sub-namespace barrel. Tracked as an opt-in follow-up.
- (−) Same-namespace relative imports are a special case the cycle
  detector, the tests, and the `PackageJsonWriter` all have to handle
  alongside `#` and `#/...`. The complexity is localized but non-zero.

## Alternatives considered

- **File-first resolution** (`from "#/issues/domain/issue"`): rejected.
  Leaks file organization as described above, and multiplies import lines.
- **Aggregated root barrel via `export * from "./**"` by default**:
  rejected. Breaks tree-shaking under every current bundler heuristic.
- **Aggregated root barrel via `export namespace { ... }` by default**:
  rejected for *default*, retained as the design for the opt-in flag.
  Mirrors C# structure perfectly for users who prioritize the namespace
  shape over bundle-size worst-case, but shipping it by default would
  silently enlarge consumer bundles.

## References

- `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs` —
  `ComputeRelativeImportPath`, `ComputeSubPath`
- `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs` —
  bucketed local + cross-package emission, `SortValuesFirst`
- `src/Metano.Compiler.TypeScript/Transformation/BarrelFileGenerator.cs`
  — leaf-only barrels
- `src/Metano.Compiler.TypeScript/Transformation/CyclicReferenceDetector.cs`
- `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs` — `"#"` alias
  and `"."` export when a root `index.ts` exists
- `tests/Metano.Tests/NamespaceTranspileTests.cs`,
  `CrossPackageImportTests.cs`, `EndToEndOutputTests.cs`
- Issue #12 — merge local imports per barrel + values-first mixed form
- Historical: `specs/namespace-imports-plan.md`,
  `specs/same-namespace-relative-imports-plan.md` (to be deleted in
  slice 4 — git history preserves them)
