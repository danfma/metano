# Quickstart: Validating Deterministic & Self-Cleaning Output

This walkthrough proves the three user stories end-to-end. Run from the repo root.

## Prerequisites

```sh
dotnet build
```

## US1 — Layout is stable across the type set

1. Single type in a sub-namespace:
   ```sh
   dotnet run --project src/Metano.Compiler.TypeScript/ -- \
     -p <project-with-only Vigiata.Contracts.Profiles.UserProfileDto>.csproj \
     -o /tmp/out --import-alias web-server-contracts --skip-package-json --dry-run
   ```
   **Expect**: `Would write: vigiata/contracts/profiles/user-profile.ts` (NOT `user-profile.ts` at root).
2. Add a second type in `Vigiata.Contracts.Serialization`; re-run the dry-run.
   **Expect**: `vigiata/contracts/profiles/user-profile.ts` is **unchanged**, plus `vigiata/contracts/serialization/…`.

✅ Pass criteria: the first type's path is identical in both runs (SC-001, SC-002).

## US2 — Incremental rebuilds prune orphans

1. Real emit (no `--clean`) of a type `Foo.Bar.Widget`; confirm `foo/bar/widget.ts` exists.
2. Rename the type to `Gadget` (path changes); rebuild WITHOUT `--clean`.
   **Expect**: `foo/bar/widget.ts` is gone, `foo/bar/gadget.ts` exists, console shows `Pruned: foo/bar/widget.ts`.
3. Drop a hand-written file `foo/bar/notes.md` into the output tree and rebuild.
   **Expect**: `notes.md` is **never** deleted (FR-007).
4. Rebuild again with no source change.
   **Expect**: cache hit, zero deletions, no churn (FR-010).

✅ Pass criteria: orphan removed, hand-written file preserved, no-op run deletes nothing (SC-003, SC-005).

## US3 — Consumer import contract holds in clean generation

1. Fully regenerate the package (`--clean`) so no orphans remain.
2. From a consumer, import via the documented path:
   ```ts
   import { UserProfile } from "#web-server-contracts/vigiata/contracts/profiles";
   ```
   Type-check. **Expect**: resolves with no dependency on any root-level orphan.
3. Confirm `package.json#exports` lists the full-namespace leaf-barrel subpath, and the previously-broken `../contracts` root import is either backed by the opt-in root barrel or replaced by the subpath import.

✅ Pass criteria: clean-regen type-check passes; the originally reported failure does not recur (SC-006, SC-007).

## Regression gate

```sh
dotnet run --project tests/Metano.Tests/                 # TUnit (regenerate goldens that declare a namespace)
cd targets/js/sample-todo && bun run build && bun test   # representative sample (and the other namespace-declaring samples)
dotnet csharpier .                                        # format gate
```

Re-run the full transpile twice and diff the output trees — they must be byte-identical (idempotent, SC-004).
