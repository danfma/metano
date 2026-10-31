# Contract: Exports subpath root

**Normative for**: FR-001–FR-007. Amends `specs/005-deterministic-output-layout/contracts/import-contract.md`.

## Declaration

```csharp
[assembly: TranspileAssembly]
[assembly: EmitPackage("@video-studio/domain", SubpathRoot = "VideoStudio.Domain")]
```

## Mapping (root `VideoStudio.Domain` → kebab path `video-studio/domain`)

| Leaf barrel on disk (unchanged) | `exports` key | Consumer specifier |
|---|---|---|
| `video-studio/domain/index.ts` | `.` | `@video-studio/domain` |
| `video-studio/domain/episodes/index.ts` | `./episodes` | `@video-studio/domain/episodes` |
| `video-studio/domain/episodes/cuts/index.ts` | `./episodes/cuts` | `@video-studio/domain/episodes/cuts` |
| `video-studio/domain-events/index.ts` | `./video-studio/domain-events` | `@video-studio/domain/video-studio/domain-events` |
| `shared/kernel/index.ts` | `./shared/kernel` | `@video-studio/domain/shared/kernel` |
| `index.ts` (opt-in root aggregation barrel) | `.` → collides with the root-namespace barrel when both exist (`MS0029`) | — |

Entry values are unchanged: `{ "source": "./src/<on-disk path>", "types": "./dist/<on-disk path>.d.ts", "import": "./dist/<on-disk path>.js" }`.

With an output prefix (`--src-root`, output in `src/domain`) the prefix is prepended after
stripping: `./domain/episodes`, `./domain` for the root.

## Unchanged

- `imports` (`#/*`, `#`, or `#<alias>/*`, `#<alias>`): still map onto the full on-disk tree.
- Internal generated-to-generated imports (direct files under the alias).
- On-disk paths and pruning.

## Diagnostics

| Code | Severity | When |
|---|---|---|
| `MS0029` | Error | `SubpathRoot` is not a dotted C# namespace (reported at the attribute; value ignored). |
| `MS0029` | Error | Two barrels map to one `exports` key; the barrel under the root keeps it, the other is omitted, CLI exits non-zero. |
