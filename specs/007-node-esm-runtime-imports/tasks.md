# Tasks: Make metano-runtime importable from Node ESM

**Input**: Design documents from `specs/007-node-esm-runtime-imports/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: The packaging smoke test (`scripts/smoke-test-runtime-package.sh`) is the test for US1–US4 (FR-004). Existing `bun test` suites are the regression guard (FR-009).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Record the baseline: `cd targets/js/metano-runtime && bun run build && bun test` (expect 286 pass / 0 fail) and confirm `node --input-type=module -e 'await import("./dist/index.js")'` fails with `ERR_UNSUPPORTED_DIR_IMPORT`

## Phase 2: Foundational (blocking — the regression guard must exist before the fix so it can be seen failing)

- [ ] T002 Create `scripts/smoke-test-runtime-package.sh` per `contracts/smoke-test.md`: build, `bun pm pack`, consumer project in `${RUNNER_TEMP:-$(mktemp -d)}`, `bun add` tarball + `typescript@5.9.3`, Node imports of `.`, `./system/json`, `./system/linq` (assert `typeof HashCode === "function"`, `SerializerContext`, `linq`), specifier scan of `dist/**/*.js`, `tsgo` + `tsc` NodeNext type-check with `skipLibCheck: false`, `lib: ["es2023"]` and one `@ts-expect-error`
- [ ] T003 Run the script against the unfixed source and confirm it fails at the Node import and specifier-scan checks (SC-003 proof)

## Phase 3: User Story 1 + 2 — Import and type-check from Node ESM (P1) 🎯 MVP

**Independent Test**: `bash scripts/smoke-test-runtime-package.sh` exits 0.

- [ ] T004 [P] [US1] Change `export * from "./system"` / `"./type-checking"` to `"./system/index.ts"` / `"./type-checking/index.ts"` in `targets/js/metano-runtime/src/index.ts`
- [ ] T005 [P] [US1] Change `export * from "./primitive-type-checks"` to `"./primitive-type-checks.ts"` in `targets/js/metano-runtime/src/type-checking/index.ts`
- [ ] T006 [P] [US1] Move `targets/js/metano-runtime/src/type-checking/primitive-type-checks.test.ts` to `targets/js/metano-runtime/test/type-checking/primitive-type-checks.test.ts`, importing from `#/type-checking/primitive-type-checks.ts`
- [ ] T007 [US2] Run the smoke script; all checks (imports, scan, tsgo, tsc) pass

## Phase 4: User Story 3 — Gate CI and release (P2)

**Independent Test**: CI `js` job shows the smoke-test step green; release.yml runs it before NuGet login.

- [ ] T008 [P] [US3] In `.github/workflows/ci.yml` `js` job: add `actions/setup-node@v4` (`node-version: "24"`) and a "metano-runtime packaging smoke test" step running `bash scripts/smoke-test-runtime-package.sh` after the runtime build/test step
- [ ] T009 [P] [US3] In `.github/workflows/release.yml`: add "Smoke-test packed metano-runtime" (unconditional, also on dry runs) immediately after "Install npm dependencies" and before "NuGet login"

## Phase 5: User Story 4 — Tighten export map (P3)

**Independent Test**: from the smoke consumer, `system/uuid` and `system/uuid.js` load; `system/collections` fails with `ERR_MODULE_NOT_FOUND`.

- [ ] T010 [US4] Replace `"./*": "./dist/*"` in `targets/js/metano-runtime/package.json` with `"./package.json": "./package.json"`, `"./*.js": "./dist/*.js"`, `"./*": "./dist/*.js"`
- [ ] T011 [US4] Extend the smoke script with deep-import assertions: `metano-runtime/system/uuid.js` and `metano-runtime/system/uuid` load; `metano-runtime/system/collections` rejects with a code other than `ERR_UNSUPPORTED_DIR_IMPORT`

## Phase 6: User Story 5 — Docs (P3)

- [ ] T012 [P] [US5] Add "The metano-runtime dependency" section to `docs/getting-started.md` (dependency declared by generated packages; registry install, Bun workspace, Vite alias / tsconfig `paths`; Node ESM works without `server.deps.inline` from the fixed version)
- [ ] T013 [P] [US5] Cross-link the section from `docs/cross-package.md`

## Phase 7: Polish & Verification

- [ ] T014 Run `bash scripts/smoke-test-runtime-package.sh`, runtime `bun test` (286 pass), sample suites (`sample-todo`, `sample-todo-service`, `sample-issue-tracker`: `bun run build && bun test`) and `bunx biome check .`
- [ ] T015 Dual-agent review (`compiler-man` + `bob`) on the diff; fix findings
- [ ] T016 Commit `fix: make metano-runtime importable from Node ESM (#249)` with `Closes #249`, push, update PR description with verification results

## Dependencies

T001 → T002 → T003 → (T004, T005, T006 in parallel) → T007 → (T008, T009, T010, T012, T013 in parallel; T011 after T010) → T014 → T015 → T016

## Implementation Strategy

MVP = Phases 1–3 (fixes the shipped defect). Phases 4–6 are independent increments; all ship in one PR because the change is small.
