# Research: Make metano-runtime importable from Node ESM

All findings below were reproduced in this session against the current branch (Node 22.23, Bun 1.4.4,
tsgo 7.0.0-dev.20260707.2, TypeScript 5.9.3).

## R1 — Root cause and fix surface

**Evidence**: `bun run build` on `main` emits `dist/index.js` = `export * from "./system"; export * from "./type-checking";`
and `dist/type-checking/index.js` = `export * from "./primitive-type-checks";`. Node:
`ERR_UNSUPPORTED_DIR_IMPORT … Did you mean to import "./system/index.js"?`. A repo-wide scan of
`src/**/*.ts` (non-test) finds exactly these three extensionless relative specifiers; every other
module already uses explicit `.ts` and is rewritten correctly. No `#/` subpath-alias imports exist in
non-test runtime source, so the `imports` field does not affect consumers.

**Decision**: Change the three specifiers to explicit `.ts`.
**Rationale**: Matches house style; `rewriteRelativeImportExtensions` then emits `.js`. Verified:
patched build → `export * from "./system/index.js"`, Node import of root/json/linq returns 87/8/48 exports.
**Alternatives considered**: switch build to `module: nodenext` (forces extensions repo-wide, larger
blast radius, changes `#/` alias semantics in tests) — rejected as disproportionate for a 3-line bug;
post-build rewrite script — rejected, hides the source of truth.

## R2 — Declaration files keep `.ts` specifiers

**Evidence**: After the fix, `dist/index.d.ts` contains `export * from "./system/index.ts"` (14 `.d.ts`
files carry `.ts` relative specifiers — pre-existing, not introduced by this fix). A NodeNext consumer
with `skipLibCheck: false` type-checks cleanly with both tsgo 7 and `tsc` 5.9.3, and a deliberate
mismatch (`const s: string = isInt32(1)`) is reported — so types are real, not `any`.
**Decision**: Accept; guard it by type-checking with both compilers in the smoke test.
**Alternatives considered**: rewriting `.d.ts` specifiers post-build — unnecessary while both compilers resolve them.

## R3 — Where the gate goes in release.yml

**Evidence**: release.yml order is: install → NuGet OIDC login → **semantic-release** (bumps, writes
CHANGELOG, commits, tags, pushes, `dotnet nuget push`, GitHub Release) → resolve tag → build runtime
→ `npm publish`. A smoke test placed next to the existing runtime build would run after the tag and
NuGet packages are already public.
**Decision**: Insert "Build metano-runtime" + "Smoke-test packed metano-runtime" right after
`bun install`, before NuGet login, unconditional (also on dry runs). Keep the later build step
(cheap, idempotent) so `sync-npm-version.sh` still finds `dist/`.
**Alternatives considered**: gating only the npm step — rejected (FR-006: NuGet and npm would diverge).

## R4 — Export map tightening

**Evidence** (prototype in scratch consumer with `"./*.js": "./dist/*.js", "./*": "./dist/*.js"`):

| Specifier | Today | Proposed |
|---|---|---|
| `metano-runtime/system/uuid.js` | OK | OK |
| `metano-runtime/system/uuid` | `ERR_MODULE_NOT_FOUND` (maps to extensionless file) | OK |
| `metano-runtime/system/collections` | `ERR_UNSUPPORTED_DIR_IMPORT` | `ERR_MODULE_NOT_FOUND` |
| `metano-runtime/package.json` | `ERR_MODULE_NOT_FOUND` (maps to `dist/package.json`) | OK (explicit entry) |

Node's pattern ordering picks `./*.js` over `./*` for `.js`-suffixed requests (longer key, same
prefix), so no `uuid.js.js` double extension. Both `tsc` and `tsgo` resolve types for the `.js` and
extensionless deep imports via sibling `.d.ts`.
**Decision**: Adopt it, plus an explicit `"./package.json": "./package.json"`.
**Alternatives considered**: delete `./*` entirely — rejected, would break existing `.js` deep imports
(docs/ADRs reference deep paths such as `system/uuid`); enumerate every module — rejected, high
maintenance with no consumer benefit.

## R5 — Smoke-test hermeticity

**Decision**: Create the consumer under `${RUNNER_TEMP:-$(mktemp -d)}`, never inside the repo (the
root `package.json` declares `workspaces: ["targets/js/*"]`; a consumer inside it could resolve
`metano-runtime` through the workspace symlink and silently test source instead of the tarball).
Install with `bun add <tarball>` + `bun add -d typescript@5.9.3`. Use the workspace's `tsgo` binary
(already installed by the job's `bun install`). Pin `lib: ["es2023"]` in the consumer tsconfig.
**Rationale for lib pin**: with `lib: esnext` + `skipLibCheck: false`, `@js-temporal/polyfill`'s own
declarations clash with the built-in `Temporal` types (TS2430) — pre-existing, third-party, unrelated
to this fix; pinning keeps the check focused on `metano-runtime` (spec edge case).

## R6 — Packed-artifact specifier scan

**Decision**: In addition to the Node import, scan the *installed* `dist/**/*.js` for relative
specifiers (`from "./…"`, `from "../…"`, `import("./…")`) not ending in `.js`, and fail listing each
offender. **Rationale**: Node only follows modules reachable from the three entry points; a future
extensionless import in a deep-only module would slip past the import check. The scan covers the
whole defect class (SC-003).

## R7 — CI Node version

**Evidence**: ci.yml `js` job installs only Bun; the runner's preinstalled Node is incidental.
**Decision**: Add `actions/setup-node@v4` with `node-version: "24"` to match release.yml.

## R8 — `skipLibCheck` in the type-check

**Alternative proposed**: run the NodeNext type-check with `--skipLibCheck` to dodge the Temporal clash.
**Decision**: Keep `skipLibCheck: false` + `lib: ["es2023"]` (R5). **Rationale**: the thing under test
*is* a library's declaration files; `skipLibCheck` suppresses diagnostics inside
`metano-runtime/dist/**/*.d.ts`, so an unresolvable `export * from "./system/index.ts"` would only
surface indirectly (as a missing member at the use site, if one is used). The lib pin removes the
Temporal noise without blinding the check. Verified clean on tsgo 7 and tsc 5.9.3.

## R9 — Misplaced runtime test

**Evidence**: `src/type-checking/primitive-type-checks.test.ts` is the only `*.test.ts` under `src/`
(13 others live in `test/`, importing via `#/….ts`) and carries the 4th extensionless relative
specifier. Not shipped (excluded by tsconfig), but it violates the `test/` convention.
**Decision**: Move to `test/type-checking/primitive-type-checks.test.ts`, import from
`#/type-checking/primitive-type-checks.ts`. Baseline: 286 pass / 0 fail across 14 files — must stay 286.
