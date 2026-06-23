# Quickstart: Emit generated code into a subfolder of an existing project

This walkthrough shows how to use the configurable import alias to generate a C#
contracts library into a subfolder of an existing TypeScript/npm project without
colliding with that project's own `#` alias.

## Scenario

- Existing frontend `frontend/` whose `src/` is the source root, already exposing
  `#/*` → `./src/*` in its `package.json`.
- C# project `Abc.Contracts` (`[assembly: TranspileAssembly]`) with types under
  namespaces like `Abc.Contracts.Serialization`.
- You want the generated TypeScript under `frontend/src/abc/contracts`, referenced
  by both your app code and by other generated types.

## Steps

### 1. Configure the output dir and alias

Via MSBuild (in `Abc.Contracts.csproj`):

```xml
<PropertyGroup>
  <MetanoOutputDir>../frontend/src/abc/contracts</MetanoOutputDir>
  <MetanoImportAlias>contracts</MetanoImportAlias>
  <MetanoClean>true</MetanoClean>
</PropertyGroup>
```

Or directly via the CLI:

```sh
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p Abc.Contracts/Abc.Contracts.csproj \
  -o frontend/src/abc/contracts \
  --import-alias contracts --clean
```

### 2. Transpile

Run the build (or the CLI command above). Metano emits the `.ts` files under
`frontend/src/abc/contracts` and merges alias entries into the nearest
`package.json` (the frontend's).

### 3. Verify the generated imports

Internal cross-namespace imports now use the isolated alias:

```ts
import { MyType } from "#contracts/serialization";
```

…and the frontend `package.json` has, side by side:

```jsonc
{
  "imports": {
    "#/*": { "default": "./src/*.ts" },          // your app's alias — untouched
    "#contracts/*": {                              // Metano-managed, depth-correct
      "types":   "./dist/abc/contracts/*.d.ts",
      "import":  "./dist/abc/contracts/*.js",
      "default": "./src/abc/contracts/*.ts"
    }
  }
}
```

### 4. Build the TypeScript

```sh
cd frontend && bun run build
```

Resolution succeeds with zero manual edits to imports, and your app's own `#/...`
imports keep working.

## Verifying the contract (acceptance checks)

- **Isolation**: your pre-existing `#`/`#/*` entries are unchanged after transpile.
- **Correct resolution**: every generated `#contracts/...` import points at a real
  emitted file.
- **No malformed paths**: no `//` appears in any `imports`/`exports` path value.
- **Backward compatible**: drop `--import-alias` / `MetanoImportAlias` and the
  output reverts byte-for-byte to the default `#`/`#/*` form.

## Notes

- If several Metano projects target the same TypeScript package, give each a
  distinct `MetanoImportAlias` (e.g. `contracts`, `events`) to avoid key clashes —
  cross-project conflict detection is not provided in this version.
- Imports of types from referenced packages (`[EmitPackage("name")]`) are
  unaffected — they continue to import from `name/...`.
