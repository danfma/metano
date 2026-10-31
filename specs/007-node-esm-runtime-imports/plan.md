# Implementation Plan: Make metano-runtime importable from Node ESM

**Branch**: `claude/peaceful-wright-ib8d4r` | **Date**: 2026-10-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-node-esm-runtime-imports/spec.md` — issue [danfma/metano#249](https://github.com/danfma/metano/issues/249), traced to **NFR-007**.

## Summary

The published `metano-runtime` fails to load under Node ESM because three relative specifiers in
the source barrels are extensionless, so `rewriteRelativeImportExtensions` has nothing to rewrite
and the emitted `dist/index.js` keeps `export * from "./system"` (a directory import). The fix has
four parts, in order of value:

1. **Source fix** — make the three outliers explicit `.ts` (`./system/index.ts`,
   `./type-checking/index.ts`, `./primitive-type-checks.ts`), matching every other runtime module.
2. **Packaging smoke test** — one script, `scripts/smoke-test-runtime-package.sh`, that packs the
   runtime with `bun pm pack`, installs the tarball into a throwaway `type: module` project outside
   the workspace, imports `.`, `./system/json`, `./system/linq` from plain Node, scans the packed
   `dist/**/*.js` for extensionless relative specifiers, and type-checks the three entry points
   under `module: nodenext` with both `tsgo` and stable `tsc` (`skipLibCheck: false`).
3. **Gate placement** — CI runs the script in the `js` job; **release runs it before
   semantic-release** (the issue says "add to release.yml", but the existing publish block runs
   *after* the tag, changelog commit and NuGet push — a gate there would fail with half a release
   already shipped).
4. **Export-map tightening + docs** — replace `"./*": "./dist/*"` with `"./*.js": "./dist/*.js"`
   + `"./*": "./dist/*.js"` and add `"./package.json"`; document the runtime dependency in
   `docs/getting-started.md` / `docs/cross-package.md`.

All four were prototyped in a scratch consumer during research (see research.md R1–R4).

## Technical Context

**Language/Version**: TypeScript (runtime source), Bash (smoke-test script), GitHub Actions YAML

**Primary Dependencies**: `@typescript/native-preview` (tsgo, build + NodeNext check), `typescript@5.9.x` (stable NodeNext check, installed only into the throwaway project), Node 24 (runtime under test — same major the release job provisions), Bun 1.x (`bun pm pack`, `bun add`)

**Storage**: N/A

**Testing**: The smoke-test script is the test (FR-004). Existing `bun test` suites of the runtime and samples are the regression guard (FR-009). No .NET change → no TUnit change.

**Target Platform**: npm consumers on Node ≥ 20 ESM, Vitest, NodeNext TypeScript; bundlers/Bun unchanged

**Project Type**: Library packaging + CI/release pipeline

**Performance Goals**: Smoke test adds < 60 s to the CI `js` job and the release job

**Constraints**: Must not require npm/npx/yarn/pnpm (constitution V); must run on release dry runs; must be hermetic w.r.t. the Bun workspace (temp project created under `$RUNNER_TEMP` / `mktemp -d`, never inside the repo, so the workspace does not capture `metano-runtime`)

**Scale/Scope**: 3 source specifiers in 2 files, 1 misplaced test file moved to `test/`, 1 `package.json` exports block, 1 new script, 2 workflow edits, 2 doc sections

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Gate | Status | Notes |
|---|---|---|
| I. Clean Code | ✅ | Script split into named functions (`pack_runtime`, `create_consumer`, `assert_node_imports`, `assert_no_extensionless_specifiers`, `assert_nodenext_types`); `set -euo pipefail`. |
| II. Expressive code | ✅ | Script and step named for intent (`smoke-test-runtime-package`). |
| III. Screaming organization | ✅ | Lives next to `scripts/sync-npm-version.sh`, the other runtime-packaging script. |
| IV. Ports & Adapters | ✅ N/A | No compiler code touched. |
| V. DX — Bun only | ✅ | Bun packs/installs. `node` is the *runtime under test*, not a package manager. `npm publish` in release is pre-existing and untouched. |
| V. DX — single command | ✅ | `bash scripts/smoke-test-runtime-package.sh` runs locally exactly as in CI. |
| VI. Pragmatism | ✅ | No new abstraction; no CJS build; no lint rule (the packed-artifact scan covers the defect class with zero new tooling). |
| Spec as source of truth | ✅ | Traces to NFR-007; no new FR needed in baseline (bug fix). |
| Dual-agent review | ⏳ | `compiler-man` + `bob` before commit of the implementation. |
| Build & tests green | ⏳ | Runtime `bun test`, sample suites, `bunx biome check .`, smoke test. |
| Worktree per issue | ⚠️ Deviation | Session is pinned to branch `claude/peaceful-wright-ib8d4r`; see Complexity Tracking. |

**Post-design re-check**: unchanged — design adds no abstractions beyond one script.

## Project Structure

### Documentation (this feature)

```text
specs/007-node-esm-runtime-imports/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R6 decisions with evidence
├── data-model.md        # Phase 1 — export-map + smoke-test entities
├── quickstart.md        # Phase 1 — reproduce, fix, verify
├── contracts/
│   ├── package-exports.md     # Public subpath contract of metano-runtime
│   └── smoke-test.md          # Script interface (inputs, exit codes, checks)
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
targets/js/metano-runtime/
├── package.json                         # exports map tightened (+ "./package.json")
└── src/
    ├── index.ts                         # ./system → ./system/index.ts, ./type-checking → ./type-checking/index.ts
    └── type-checking/
        ├── index.ts                     # ./primitive-type-checks → ./primitive-type-checks.ts
        └── primitive-type-checks.test.ts  # MOVED → test/type-checking/primitive-type-checks.test.ts
test/type-checking/
└── primitive-type-checks.test.ts        # NEW location; imports "#/type-checking/primitive-type-checks.ts"
                                         # (the only test living under src/ and the 4th extensionless specifier)

scripts/
├── sync-npm-version.sh                  # unchanged
└── smoke-test-runtime-package.sh        # NEW — pack → install → node import → scan → nodenext type-check

.github/workflows/
├── ci.yml                               # js job: setup-node 24 + smoke-test step after runtime build
└── release.yml                          # build runtime + smoke test BEFORE NuGet login / semantic-release

docs/
├── getting-started.md                   # "The metano-runtime dependency" section
└── cross-package.md                     # cross-link + workspace/alias note
```

**Structure Decision**: Single script shared by both workflows (DRY); no changes to the .NET
solution, the compiler, or generated sample output.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| No worktree per issue | Session harness pins work to `claude/peaceful-wright-ib8d4r` and forbids creating other branches | Creating `../Metano-issue-249` on a new branch would violate the session's branch constraint |
| Two type-checkers in the smoke test (`tsgo` + stable `tsc`) | Packed `.d.ts` files keep `.ts` relative specifiers (the rewrite only touches JS emit); stable-TS consumers are the majority and must be proven, not assumed | tsgo-only would leave the larger consumer population untested (research R2) |
