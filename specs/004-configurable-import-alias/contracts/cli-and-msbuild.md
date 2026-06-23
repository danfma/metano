# Contract: CLI flag & MSBuild property

The configuration surface for the import alias. Mirrors the existing
`--dist` / `--src-root` / `--package-root` pattern.

## CLI: `--import-alias`

Command: `metano-typescript` (`src/Metano.Compiler.TypeScript/Commands.cs`).

| Property | Value |
|----------|-------|
| Flag | `--import-alias <name>` |
| Parameter | `string? importAlias = null` |
| Default | `null` (alias unset → legacy `#` behavior) |
| Normalization | Trim; strip one leading `#`; blank → unset |

**Behavior**:
- Unset → generated internal imports and `package.json` use `#` / `#/*` (unchanged).
- Set (e.g. `--import-alias contracts`) → internal imports use `#contracts/...`,
  `package.json` gets `#contracts` / `#contracts/*` only; host `#` untouched.

**Example**:

```sh
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p samples/Abc.Contracts/Abc.Contracts.csproj \
  -o ../frontend/src/abc/contracts \
  --import-alias contracts --clean
```

## MSBuild: `MetanoImportAlias`

File: `src/Metano.Build/build/Metano.Build.targets`.

| Property | Value |
|----------|-------|
| Property name | `MetanoImportAlias` |
| Maps to | `--import-alias "$(MetanoImportAlias)"` |
| Condition | Emitted only when `'$(MetanoImportAlias)' != ''` |

**Example (`.csproj`)**:

```xml
<PropertyGroup>
  <MetanoOutputDir>../frontend/src/abc/contracts</MetanoOutputDir>
  <MetanoImportAlias>contracts</MetanoImportAlias>
</PropertyGroup>
```

## Contract guarantees

- Providing the value with or without a leading `#` yields the same alias key.
- A blank/whitespace value is equivalent to not setting it.
- Changing the value forces regeneration (the alias participates in the incremental
  cache fingerprint).
