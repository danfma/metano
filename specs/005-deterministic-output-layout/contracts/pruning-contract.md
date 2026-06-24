# Contract: Orphan Reconciliation (Self-Cleaning Builds)

**Normative for**: FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-014; R5, R6, R8.

## Behavior

On a **real emit** (not a cache hit), after the current output set is computed and written:

```
previous = manifest(prior .metano-cache.json).outputPaths   # files Metano produced last run
current  = paths of files produced this run                 # type files + barrels
orphans  = previous − current
for each o in orphans: delete <outputDir>/o
remove now-empty generated directories (excluding the output root and Metano metadata files)
write fresh manifest (OutputHashes + layout-versioned fingerprint)
```

## Guarantees

- **Ownership** (FR-007): only paths present in the **prior manifest** are eligible for deletion. Hand-written or unrelated files inside the output directory are never deleted — even if they sit next to generated files.
- **Barrels & dirs** (FR-011): orphaned leaf/root barrels and directories emptied by the prune are removed too.
- **No churn on no-op** (FR-010): an identical output set yields an empty `orphans` set — nothing is deleted.
- **Failure-safe** (FR-014): reconciliation runs only after a successful generation+write and is paired with rewriting the manifest; a failed run leaves prior files and the prior manifest intact.
- **Reporting** (FR-009): each removed path is printed (e.g. `Pruned: <relativePath>`).

## Interaction matrix

| Mode | Manifest read | Prune runs? | Notes |
|---|---|---|---|
| Incremental, sources changed (cache miss) | yes | yes | the normal self-cleaning path |
| Incremental, nothing changed (cache hit) | n/a | no | no emit → no orphan can appear |
| `--clean` | n/a | no (moot) | whole output dir wiped first |
| `--no-cache` | no | no | no manifest available; document "pair with `--clean`" |
| First run (no prior manifest) | empty | no-op | nothing to reconcile |

## Cache invalidation on upgrade (R6)

The TypeScript target's `ConfigurationFingerprint` includes a `layout=full-namespace-v…` token. A compiler upgrade that changes the layout bumps the token → the cache-hit gate fails → a full re-emit runs → old-layout files become orphans and are pruned in the same run. Without this, old files would still hash-match and a cache hit would silently preserve the old layout.

## Placement (R8)

Reconciliation lives in `TranspilerHost` (target-agnostic core), so both the CLI and the MSBuild integration inherit it with no `Metano.Build.targets` change. `MetanoClean` (full wipe) remains available.
