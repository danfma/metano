# Contract: `scripts/smoke-test-runtime-package.sh`

**Invocation**: `bash scripts/smoke-test-runtime-package.sh` from any directory (resolves repo root from its own path).

**Preconditions**: `bun install` already run at repo root (provides `tsgo`); `node` ≥ 20 on PATH.
The script runs `bun run build` itself so it never tests a stale `dist/`.

**Environment**: `RUNNER_TEMP` (optional) — parent dir for the throwaway consumer; falls back to `mktemp -d`. Temp dir is removed on exit (trap).

**Checks, in order** (first failure exits non-zero with a labelled message):

1. Build + `bun pm pack` the runtime → tarball path.
2. Create consumer project outside the repo; `bun add <tarball>`; `bun add -d typescript@5.9.3`.
3. `node --input-type=module` imports `metano-runtime`, `metano-runtime/system/json`, `metano-runtime/system/linq`; asserts each module has ≥ 1 export.
4. Scan `node_modules/metano-runtime/dist/**/*.js` for relative specifiers not ending in `.js`; print `file:line: specifier` for each.
5. `tsgo -p .` and `tsc -p .` with NodeNext config; `check.ts` includes one `@ts-expect-error` so a silently-`any` resolution fails the check.

**Exit codes**: `0` all checks passed; non-zero otherwise.

**Callers**: `.github/workflows/ci.yml` (`js` job, after runtime build/test), `.github/workflows/release.yml` (before NuGet login and semantic-release, unconditional).
