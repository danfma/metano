# Cross-Project References

Metano supports sharing types across **multiple C# projects** that each produce
their own npm package. This guide explains how it works and how to set it up.

## The scenario

You have a solution with multiple C# projects:

```
MySolution/
├── MySolution.Shared/          # → npm: @acme/shared
├── MySolution.Users/           # → npm: @acme/users (depends on Shared)
└── MySolution.Orders/          # → npm: @acme/orders (depends on Shared + Users)
```

Each project transpiles to its own npm package. Types in `Orders` can reference
types in `Users` and `Shared`, and the generated TypeScript gets the right
`import` statements and `package.json` entries **automatically**.

## Setup

### Each project declares its npm package identity

In each `.csproj`, add an `AssemblyInfo.cs` (or similar) with:

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("@acme/shared", Version = "workspace:*")]
```

- **`[assembly: TranspileAssembly]`** — marks the assembly for whole-project
  transpilation (optional, but usually what you want)
- **`[assembly: EmitPackage(name, Version)]`** — the npm package name and the
  version specifier to use in consumer `package.json#dependencies`

The `Version` field is optional. Common values:

- `"workspace:*"` — for Bun/pnpm/Yarn workspace siblings
- `"^1.2.3"` — for a published npm version
- Omit entirely — Metano falls back to `workspace:*` if the assembly has no
  explicit `Version`, or `^Major.Minor.Patch` from the assembly's
  `Identity.Version` otherwise

### Consumer project references the producer via `ProjectReference`

In `MySolution.Users.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../MySolution.Shared/MySolution.Shared.csproj" />
</ItemGroup>
```

This is a **normal .NET project reference**. Metano picks up the reference
during compilation, walks the referenced assembly's public types, and registers
them in its cross-assembly type map.

The TypeScript CLI also finds the target package root by walking upward from
`MetanoOutputDir` until it sees a `package.json`. The walk never goes above the
topmost directory of the workspace, so a `package.json` that belongs to another
project further up is left alone. Starting at the output folder itself, that
topmost directory is:

1. the nearest repository root (it holds `.git`);
2. else the nearest solution directory (it holds a `.sln` or `.slnx` file);
3. else the C# project directory, when it holds the output folder;
4. else the folder just below the directory that the output and the project
   share, on the output's side (the shared directory itself is often a generic
   folder such as `~` or `/tmp`, so it is never used; the filesystem root does
   not count as shared);
5. else (the output shares only the filesystem root with the project) the
   output folder itself.

If no package file is found, it falls back to the legacy convention: the parent
directory of the output folder. When that parent lies above the topmost
directory (the output folder is the topmost directory itself), the run fails
with `MS0044` when files were generated: the `.ts` files are written,
`package.json` is not (a run that generated nothing skips `package.json`
silently). Layouts without `.git` or a solution file whose `package.json` sits
in the directory shared with the C# project now need `MetanoPackageRoot`. Set
`MetanoPackageRoot` (`--package-root`) when your generated files live somewhere
unusual; an explicit root is used as given. Note that a home directory kept
under git (dotfiles) counts as a repository root, so the walk can reach a
`~/package.json` there, and that a monorepo's root `package.json` is used when
the generated package has none of its own.

## What Metano generates

When a consumer uses a type from a referenced project, the transpiler:

1. **Resolves the type origin** — finds the producer assembly's `[EmitPackage]`
   name + the type's namespace path.
2. **Emits an `import` statement** — `import { TypeName } from "@acme/shared/namespace/path"`
3. **Adds a `package.json` dependency** — the consumer's generated `package.json`
   gets `"@acme/shared": "workspace:*"` automatically.
4. **Merges multiple names from the same barrel** — if you import `Money`,
   `Currency`, and `Price` all from `@acme/shared/finance`, they get one combined
   import line.

### Example

**`Shared/Money.cs`:**

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("@acme/shared")]

namespace Acme.Shared.Finance;

public record Money(decimal Amount, string Currency);
```

**`Users/User.cs`:**

```csharp
using Acme.Shared.Finance;

namespace Acme.Users;

public record User(string Id, string Name, Money Balance);
```

**Generated `@acme/users/src/user.ts`:**

```typescript
import { HashCode } from "metano-runtime";
import { Money } from "@acme/shared/finance";

export class User {
  constructor(
    readonly id: string,
    readonly name: string,
    readonly balance: Money,
  ) {}
  // …
}
```

**Generated `@acme/users/package.json`:**

```json
{
  "name": "@acme/users",
  "dependencies": {
    "@acme/shared": "workspace:*",
    "metano-runtime": "^2.6.1"
  }
}
```

Both `@acme/shared` and `metano-runtime` are ordinary dependencies: they resolve only when the generated package
is installed as a workspace member (or the runtime is otherwise installed or aliased).
See [How generated code resolves `metano-runtime`](getting-started.md#how-generated-code-resolves-metano-runtime).

## Namespace-first import resolution

Metano lays generated files out by their **full** kebab-cased C# namespace under
the package root — the on-disk path mirrors the fully-qualified name, with nothing
stripped (ADR-0025). The package name and the namespace are therefore both present
in a cross-package import:

| Producer type | Generated import |
|---|---|
| `Acme.Shared.Finance.Money` | `from "@acme/shared/acme/shared/finance"` |
| `Acme.Shared.Finance.Currency` | `from "@acme/shared/acme/shared/finance"` |

A cross-package import resolves to the namespace's **leaf barrel** (`index.ts`
inside that namespace directory), so multiple names from the same namespace merge
onto one import line. (This mirrors how the producing package emits its own files,
which keeps the two layouts in lock-step.)

**Within a single package**, references between generated types import the
**defining file directly** — `from "#/acme/shared/finance/money"` across
namespaces, or `from "./money"` within the same namespace — never through a barrel.
This keeps internal correctness independent of barrel emission and structurally
avoids ESM import cycles.

A single **opt-in** root aggregation barrel (`--namespace-barrels` /
`MetanoNamespaceBarrels`) re-exports the whole hierarchy under nested
`export namespace` blocks for consumers who prefer one entry point. It is opt-in
because, unlike the per-namespace leaf barrels, it defeats tree-shaking.

### Shortening package subpaths with `SubpathRoot`

When the namespaces already carry the package identity, the full-namespace
subpath repeats it (`@acme/shared/acme/shared/finance`). Declare the namespace
prefix that the package name stands for, and Metano strips it from the
`package.json#exports` keys **and** from every consumer's import:

```csharp
[assembly: EmitPackage("@acme/shared", SubpathRoot = "Acme.Shared")]
```

| Producer namespace | On-disk barrel (unchanged) | `exports` key | Consumer import |
|---|---|---|---|
| `Acme.Shared.Finance` | `src/acme/shared/finance/index.ts` | `./finance` | `from "@acme/shared/finance"` |
| `Acme.Shared` | `src/acme/shared/index.ts` | `.` | `from "@acme/shared"` |
| `Acme.SharedKernel` (no segment match) | `src/acme/shared-kernel/index.ts` | `./acme/shared-kernel` | `from "@acme/shared/acme/shared-kernel"` |

Only the package-facing subpaths change: generated files stay under their
full-namespace folders, the `#/*` alias and internal imports are untouched, and
each entry's `source` / `types` / `import` targets still point at the real
files. Matching is by whole namespace segment. The root lives on the attribute
(not in MSBuild) because consumers read it from the referenced assembly, which
keeps both sides in agreement. See spec
[`008-exports-subpath-root`](../specs/008-exports-subpath-root/spec.md).

## Multiple packages, same type name

If two referenced assemblies both declare a public type called `User`, Metano
disambiguates at the **symbol level** (via Roslyn), not by string matching. Each
`User` ends up correctly imported from its own producing package — no collision.

## Publishing-friendly flow

When you publish your packages to npm:

1. Set `[assembly: EmitPackage(name, Version = "^x.y.z")]` with the concrete
   published version
2. Consumers see `"@acme/shared": "^x.y.z"` in their generated `package.json`
3. The normal npm/yarn/pnpm/bun install flow resolves dependencies

For monorepo development, use `Version = "workspace:*"` (or omit `Version`
entirely to let it fall back automatically) so Bun/pnpm/Yarn workspaces link
the packages symbolically.

## Consuming a generated package from an app

Generated code imports helpers from `metano-runtime`. When any emitted file does,
the generated `package.json` lists `metano-runtime` under `peerDependencies`
(`^X.Y.Z`, the compiler's own release version; builds without a published
version, such as local preview builds, write `*`), so the app supplies the one
shared copy. A single copy matters: runtime classes such as `HashSet` and
`Grouping` are checked with `instanceof`, which fails across duplicates.

The writer never overrides your choice. If your `package.json` already declares
`metano-runtime` in `dependencies`, `devDependencies` or `optionalDependencies`,
or in `peerDependencies` with a value of your own (`workspace:*`, `~2.7`,
`>=2`, ...), it is left untouched. A `peerDependencies` entry that has the
writer's own shape (`*` or `^X.Y.Z`) is treated as the writer's and refreshed to
the current compiler version on regeneration. Nothing is added when no emitted
file imports the runtime, nor to a package that is itself `metano-runtime`.

Two layouts are supported.

**1. A Bun workspace with the generated package next to the app.** Generate into
a package under `generated/` (its own `package.json`, see
[Getting Started](getting-started.md)) and link it from the root:

```jsonc
// package.json (workspace root)
{ "private": true, "workspaces": ["web", "generated/*"] }

// web/package.json
{
  "dependencies": {
    "domain-ts": "workspace:*",
    "metano-runtime": "^X.Y.Z"
  }
}
```

The app's `metano-runtime` dependency satisfies the generated package's peer.
Avoid `bun add file:../generated/domain-ts`: it installs per-file symlinks, so
files generated later do not show up until you reinstall. `workspace:*` links the
whole directory.

`generated/` is normally gitignored and only exists after `dotnet build`, so on
a fresh clone build the C# project **before** `bun install`; otherwise Bun finds
no `generated/domain-ts/package.json` to link. (Alternatively, commit a
placeholder `package.json` there; the writer merges into it.)

#### Resolving the generated sources: a package-specific condition

Every managed `exports` and `imports` entry lists a source condition first (the
`.ts` sources), then `types` / `import` (the compiled `dist`). By default the
condition is called `source`. That name is generic: a consumer that enables
`source` for its bundler or type checker also opts every third-party package that
exports `source` (for example `@internationalized/date`) into raw-source
resolution, and their `.ts` is then type-checked under the app's strict settings
(`TS6133`, `TS7053`). Neither Vite nor TypeScript can scope a condition to one
package, so name the condition after the package instead. The recommended name is
`metano-source`:

```xml
<MetanoSourceCondition>metano-source</MetanoSourceCondition>
```

(CLI: `--source-condition metano-source`.) Then enable exactly that name in the
app:

```ts
// vite.config.ts
import { defaultClientConditions, defaultServerConditions, defineConfig } from "vite";

export default defineConfig({
  resolve: { conditions: ["metano-source", ...defaultClientConditions] },
  ssr: { resolve: { conditions: ["metano-source", ...defaultServerConditions] } },
});
```

```jsonc
// tsconfig.json
{ "compilerOptions": { "customConditions": ["metano-source"] } }
```

Only the Metano package resolves to sources; packages that export `source` keep
resolving to their built output. The default stays `source`, so existing
consumers are unaffected until they set the property.

The name may contain letters, digits, `-`, `_`, `.` and `:`. It must not be
all digits (Node rejects numeric `exports` keys), a Node built-in condition
(`import`, `require`, `default`, `types`, `node`, `browser`, `node-addons`,
`module-sync`), or a condition bundlers enable by default (`module`,
`development`, `production`: choosing one would route the package to its
sources in ordinary builds). Otherwise the run fails with
[`MS0059`](#ms0059--invalid-source-condition).

Switching from the default to a custom name is safe. On that build the writer
adds the new condition and removes the `source` entry it wrote before, in
`exports` and `imports`, and only when `source` still points at the exact `.ts`
file the writer emits now. Every other condition you own (`bun`,
`development`, `require`, a retargeted `source`, ...) is left alone, and in a
steady-state build nothing is ever removed. The writer does not remember an
earlier custom name, so after renaming one custom name to another, or going back
to `source`, delete the old key yourself.

Changing only `MetanoSourceCondition` (for example in `Directory.Build.props` or
with `-p:`) may not re-run the transpile target, whose inputs are the compile
items and the project file. Touch the `.csproj` or delete the output
directory's `.metano-stamp` after changing it.

Two requirements of this layout:

- **TypeScript 6.0 or newer.** The generated sources import each other through the
  package's `#/...` subpath imports, and resolving those from a consuming
  project needs TypeScript 6.0+; with 5.9.3 the consumer reported `TS2307`
  (cannot find module) on every `#/...` import. This minimum comes from that
  report, not from a version bisect.
- **A fresh-clone order**, described above: `dotnet build` first, then
  `bun install`.
- **Every consumer needs the condition.** The generated `exports` have no
  `default` target and the package has no built `dist/`, so any tool that
  resolves the package (Vite, `tsc`, Vitest, a plain Node or Bun script) must
  enable the source condition.

Right after a release, NuGet may report the new compiler version as missing even
though the package is already listed: the flat container is updated first, and
`dotnet tool restore` resolves the version only once NuGet's registration index
lists it, a few minutes later. Wait for that (`dotnet tool restore
--no-http-cache` alone is not always enough), then restore again.

**2. Emitting inside the app's source tree, with no separate package.** Point
`MetanoOutputDir` at a folder in the app and set `MetanoImportAlias` so generated
imports use their own `#<alias>` key instead of the app's `#`:

```xml
<MetanoOutputDir>../web/src/domain</MetanoOutputDir>
<MetanoImportAlias>domain</MetanoImportAlias>
```

The generated entries are merged into the app's own `package.json`. If the app
does not declare `metano-runtime`, the writer adds the `peerDependencies` entry
described above; an app should move it to a regular `dependencies` entry,
after which the writer leaves it alone.

## Diagnostics you might hit

### `MS0007` — Cross-package type without `[EmitPackage]`

If a referenced assembly has `[assembly: TranspileAssembly]` but **no**
`[assembly: EmitPackage]`, the transpiler doesn't know what npm package name to
use for imports. You'll get one `MS0007` error per type that's referenced from
that assembly.

**Fix:** add `[assembly: EmitPackage("name")]` to the producer.

### `MS0008` — Conflicting namespaces under the same `[EmitInFile]` name

If two types share `[EmitInFile("foo")]` but live in different C# namespaces,
Metano rejects the setup because consumers can't resolve which file to import
from. Move them into the same namespace or use different file names.

### `MS0029` — Invalid `SubpathRoot`

Reported (error, fails the producer's build) when `[EmitPackage(SubpathRoot = …)]`
is not a dotted C# namespace. Consumers of such a package keep full-namespace
subpaths.

### `MS0059` — Invalid source condition

Reported (error, fails the run before transpiling) when
`MetanoSourceCondition` / `--source-condition` is not a usable condition name:
it contains a character other than letters, digits, `-`, `_`, `.` and `:`, it is
all digits, it is a Node built-in condition such as `import`, `require`,
`default`, `types`, `node` or `browser`, or it is a bundler default (`module`,
`development`, `production`). Pick a package-specific name such as `metano-source`; see
[Resolving the generated sources](#resolving-the-generated-sources-a-package-specific-condition).

### `MS0030` — Two barrels claim one `exports` key

Reported (error) when, under a `SubpathRoot`, two namespace barrels would be
exported under the same key — for example `Acme.Shared.Finance` and a top-level
`Finance` both claiming `./finance`. The run stops before any file is written.

**`--namespace-barrels` + types in the root namespace always collide on `.`**:
the opt-in root aggregation barrel and the root namespace's own barrel both
claim the package root. Drop `--namespace-barrels`, narrow `SubpathRoot` to a
deeper namespace, or move the root namespace's types into a child namespace.

## The `.metalib` future (NuGet packaging)

The current cross-project flow works for **source-available** dependencies (via
`ProjectReference` in the same solution, or source packages). For NuGet packages
that don't include source, Metano needs a metadata sidecar file called
`.metalib` to provide the type signatures without full source access.

**Status:** not yet implemented. Tracked as
[issue #27](https://github.com/danfma/metano/issues/27) — schema, generation,
embedding, and consumption. The design rationale for reusing Roslyn compilation
references as the primary cross-assembly channel (and leaving `.metalib` as an
additive follow-up) is captured in
[ADR-0004](adr/0004-cross-project-references-via-roslyn.md).

For now, share source via `ProjectReference` within the same solution or via a
monorepo.

## See also

- [Attribute Reference](attributes.md) — `[EmitPackage]`, `[EmitInFile]` details
- [Architecture Overview](architecture.md) — how cross-assembly discovery works internally
- [SampleTodo.Service sample](../samples/SampleTodo.Service/README.md) — real cross-project example
