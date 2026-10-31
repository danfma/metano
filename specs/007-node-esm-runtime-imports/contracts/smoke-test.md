# Contract: `scripts/smoke-test-runtime-package.sh`

**Invocation**: `bash scripts/smoke-test-runtime-package.sh` from any directory (resolves repo root from its own path).

**Preconditions**: `bun install` already run at repo root (provides `tsgo`); `node` on PATH (24 in CI and release; ≥ 20 works); registry access for the consumer install.
The script runs `bun run build` itself so it never tests a stale `dist/`.

**Environment**: `RUNNER_TEMP` (optional) — parent dir for the throwaway consumer; falls back to `mktemp -d`. Temp dir is removed on exit (trap).

**Checks, in order** (first failure exits non-zero with a labelled message):

0. `assert_prerequisites` — `tsgo` exists at the repo root and `node` is on PATH.
1. `pack_runtime` — `bun run build` + `bun pm pack` the runtime → tarball path.
2. `create_consumer` — create the consumer project outside the repo (`package.json`, `tsconfig.json`, `entry-points.ts`); `bun add <tarball>`; `bun add -d typescript@5.9.3`; fail if the installed package has no `dist/`.
3. `assert_node_imports` — `node --input-type=module` imports `metano-runtime`, `metano-runtime/system/json`, `metano-runtime/system/linq`, `metano-runtime/system/uuid.js` and `metano-runtime/system/uuid`; asserts each exposes its expected export (`HashCode`, `SerializerContext`, `linq`, `UUID`, `UUID`).
4. `assert_directory_subpath_rejected` — importing `metano-runtime/system/collections` MUST reject with exactly `ERR_MODULE_NOT_FOUND` (fails if it resolves or rejects with any other code, notably `ERR_UNSUPPORTED_DIR_IMPORT`).
5. `assert_no_extensionless_specifiers` — scan `node_modules/metano-runtime/dist/**/*.js` for relative specifiers not ending in `.js`; print `file:line: specifier` for each.
6. `assert_nodenext_types` — `tsgo -p .` and `tsc -p .` with the NodeNext config; `entry-points.ts` has one `@ts-expect-error` per imported entry point (root, `system/json`, `system/linq`, both `system/uuid` forms), so any entry point that silently resolves to `any` fails the check.

**Exit codes**: `0` all checks passed; non-zero otherwise.

**Callers**: `.github/workflows/ci.yml` (`js` job, after runtime build/test), `.github/workflows/release.yml` (before NuGet login and semantic-release, unconditional).
