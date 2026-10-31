# Feature Specification: Make metano-runtime importable from Node ESM

**Feature Branch**: `claude/peaceful-wright-ib8d4r` (spec directory `007-node-esm-runtime-imports`; the git-extension branch hook was intentionally skipped)

**Created**: 2026-10-31

**Status**: Draft

**Input**: User description: "Fix issue #249 — Make metano-runtime importable from Node ESM. `metano-runtime` 2.6.1 cannot be imported by Node ESM: `dist/index.js` does `export * from \"./system\"` (directory import). Make the remaining extensionless relative specifiers explicit, add a packaging smoke test to the release and CI workflows, consider tightening the `./*` wildcard export, and document that generated packages declare `metano-runtime` as a dependency that must be installed to resolve."

**Traceability**: Bug fix against **NFR-007** ("Output shall be compatible with the modern TypeScript ecosystem without requiring a proprietary runtime environment") — GitHub issue [danfma/metano#249](https://github.com/danfma/metano/issues/249).

## Overview

The published `metano-runtime` package can only be loaded by bundlers and Bun, which tolerate extensionless and directory specifiers. Plain Node.js ESM — and every tool that delegates resolution to it (Vitest's external-dependency path, `tsx`, Node's test runner, server-side Hono on Node) — rejects the package entry point with `ERR_UNSUPPORTED_DIR_IMPORT`. Because almost every generated TypeScript file imports from `metano-runtime`, **every Metano-generated package is unusable on plain Node** today. Consumers currently work around it with Vitest `server.deps.inline`, which hides the problem instead of fixing it.

Reproduced locally against the current `main` build: `import("metano-runtime")` from Node 22 fails with `Directory import '…/dist/system' is not supported resolving ES modules`. Three source specifiers are responsible (two in the package root barrel, one in the type-checking barrel); all other relative imports already carry explicit extensions.

The defect shipped because nothing in CI or release ever loads the *packed* artifact the way a consumer does — all existing tests import the runtime from source via Bun.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import the runtime from plain Node ESM (Priority: P1)

A developer installs `metano-runtime` (directly, or transitively through a Metano-generated package) into a Node project with `"type": "module"` and imports the package root and its documented subpaths. The imports succeed without any bundler, loader flag, or test-runner inlining workaround.

**Why this priority**: This is the defect. Without it, generated code cannot run on Node at all.

**Independent Test**: Pack the runtime, install the tarball into an empty `type: module` project, and import `metano-runtime`, `metano-runtime/system/json`, and `metano-runtime/system/linq` from Node. All three load and expose their exports.

**Acceptance Scenarios**:

1. **Given** the packed runtime installed in a fresh ESM project, **When** Node imports the package root, **Then** the import succeeds and exposes the root exports (e.g., `HashCode`, the primitive type checks).
2. **Given** the same project, **When** Node imports `metano-runtime/system/json` and `metano-runtime/system/linq`, **Then** both imports succeed.
3. **Given** a Vitest project that consumes a generated package, **When** tests run without `server.deps.inline` for `metano-runtime`, **Then** the runtime loads.

---

### User Story 2 - Type-check against the published runtime under Node module resolution (Priority: P1)

A developer whose TypeScript project uses Node16/NodeNext module resolution imports the runtime root and subpaths and gets correct types (not `any`), with library checking enabled.

**Why this priority**: Runtime loading without matching type resolution just moves the failure from Node to the type-checker. Both must hold for the package to be consumable.

**Independent Test**: In the same scratch project, type-check a file that imports the root and both subpaths under NodeNext resolution with library checking on; zero errors from `metano-runtime` declarations, and a deliberate type mismatch against a runtime export is reported.

**Acceptance Scenarios**:

1. **Given** a NodeNext TypeScript project, **When** it type-checks imports of the root and both documented subpaths, **Then** no resolution or declaration errors originate in `metano-runtime`.
2. **Given** the same project, **When** a runtime export is used with a wrong type, **Then** the type-checker reports it (proving declarations resolved to real types).

---

### User Story 3 - A broken package can never be published again (Priority: P2)

A maintainer triggers a release. Before anything irreversible happens (version tag, changelog commit, NuGet push, npm publish), the pipeline packs the runtime and runs the consumer smoke test from Stories 1 and 2. If it fails, the release stops with nothing published. The same smoke test runs on every pull request.

**Why this priority**: The P1 fix repairs today's artifact; this story prevents the regression class. It ranks below P1 only because it does not change what users receive.

**Independent Test**: Reintroduce one extensionless barrel specifier on a branch; the CI smoke test fails with the directory-import error. Revert; it passes.

**Acceptance Scenarios**:

1. **Given** a pull request that reintroduces a directory or extensionless relative specifier in the runtime, **When** CI runs, **Then** the packaging smoke test fails.
2. **Given** a release run whose packed runtime fails the smoke test, **When** the workflow executes, **Then** no tag, release commit, NuGet package, or npm package is produced.
3. **Given** a release dry run, **When** the workflow executes, **Then** the smoke test still runs (so dry runs validate packaging too).

---

### User Story 4 - The package does not advertise subpaths that cannot load (Priority: P3)

A developer deep-importing the runtime gets either a working module or a clear module-not-found error — never a path that the package's export map promises but Node cannot load (a directory, or an extensionless file target).

**Why this priority**: The catch-all subpath export currently maps any subpath straight onto `dist/`, so extensionless and directory deep-imports are "exported" yet fail at load time. Lower priority because the documented entry points (root, `system/json`, `system/linq`) do not depend on it.

**Independent Test**: From the scratch project, import a file-level deep path (with and without `.js`) and a directory deep path; the file paths load and the directory path fails with a plain module-not-found resolution error rather than an unsupported-directory-import error.

**Acceptance Scenarios**:

1. **Given** the packed runtime, **When** a consumer imports an existing file-level subpath with an explicit `.js` extension, **Then** it continues to load (no regression for existing deep imports).
2. **Given** the packed runtime, **When** a consumer imports a subpath that names a directory without an explicit export, **Then** resolution fails as module-not-found, not as an unsupported directory import.

---

### User Story 5 - Consumers understand how the runtime dependency resolves (Priority: P3)

A developer reading the getting-started / cross-package docs learns that every generated package declares `metano-runtime` as a dependency and that it must be installed (workspace member, registry install, or alias) for imports to resolve, and that no Node/Vitest workaround is needed.

**Why this priority**: Documentation only; reduces support churn once the fix ships.

**Independent Test**: The docs contain a section stating the dependency, the supported ways to satisfy it, and the minimum runtime version that works on plain Node.

**Acceptance Scenarios**:

1. **Given** the getting-started guide, **When** a developer reads the runtime-dependency section, **Then** it states that generated packages depend on `metano-runtime`, how to satisfy it inside a workspace or via alias, and that Node ESM works without inlining workarounds from the fixed version onward.

---

### Edge Cases

- A **new barrel or module** added to the runtime later with an extensionless relative import: the smoke test must catch it (it imports the packed artifact, not source).
- **Deep imports** of the runtime that consumers already use with explicit `.js` (`metano-runtime/system/uuid.js`): must keep working after the export map is tightened.
- **Declaration files** retain source-style `.ts` relative specifiers after build (the extension rewrite applies to emitted JavaScript only): types must still resolve for NodeNext consumers on both the current stable TypeScript and the native preview compiler.
- **CommonJS consumers** (`require("metano-runtime")`): out of scope — the package is ESM-only and stays so.
- **Release dry run**: the smoke test must run even though publishing is skipped.
- **Third-party declaration conflicts** (the Temporal polyfill's declarations clash with `lib: esnext` when library checking is on): must not make the smoke test flaky or be misattributed to this fix; the smoke test pins a lib setting that isolates `metano-runtime`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every relative module specifier in the runtime's emitted JavaScript MUST name a concrete file with its extension (no directory or extensionless relative specifiers).
- **FR-002**: The packed runtime MUST be importable from Node ESM (current LTS) at the package root and at the documented subpaths `system/json` and `system/linq`, with no loader flags or bundler.
- **FR-003**: The packed runtime's type declarations MUST resolve under NodeNext module resolution for the root and documented subpaths, with library checking enabled, yielding real (non-`any`) types.
- **FR-004**: A packaging smoke test MUST exercise the packed tarball (not the source tree) by installing it into an isolated ESM project and performing FR-002 and FR-003 checks.
- **FR-005**: The smoke test MUST run on every pull request and push to `main` (CI) and MUST fail the job on any import or type-check error.
- **FR-006**: The release workflow MUST run the smoke test before any irreversible step (version tag, release commit, NuGet push, npm publish), including on dry runs, and MUST abort the release on failure.
- **FR-007**: The package export map MUST NOT map a subpath onto a target that Node cannot load (directories, extensionless files); file-level deep imports that already work with explicit `.js` MUST keep working.
- **FR-008**: Consumer documentation MUST state that generated packages declare `metano-runtime` as a dependency, the supported ways to satisfy it (registry install, workspace member, alias), and that plain Node ESM works from the fixed version onward without test-runner inlining.
- **FR-009**: Existing runtime tests and all sample end-to-end suites MUST continue to pass unchanged.

### Key Entities

- **Packed runtime artifact**: the tarball that would be published to npm; the unit under test for FR-002–FR-004.
- **Export map**: the package's public subpath contract — root, documented subpaths, and the deep-import pattern.
- **Smoke-test consumer project**: a throwaway ESM project, created per run, that installs the packed artifact and imports/type-checks it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 3 of 3 documented entry points (root, `system/json`, `system/linq`) load from plain Node ESM using the packed artifact (today: 1 of 3 fails at the root, which every generated file uses).
- **SC-002**: 0 type errors originating in `metano-runtime` declarations when a NodeNext project type-checks the 3 entry points with library checking enabled, on both the stable and native-preview TypeScript compilers.
- **SC-003**: Reintroducing a single extensionless relative specifier into the runtime makes CI fail within the same run (100% detection for this defect class).
- **SC-004**: A failing smoke test during release produces 0 published artifacts (no tag, no NuGet package, no npm version).
- **SC-005**: Consumers no longer need any test-runner inlining or loader workaround to use generated packages on Node (workaround count in docs and samples: 0).

## Assumptions

- The runtime remains ESM-only; adding a CommonJS build is out of scope.
- "Current LTS" Node is the version the release workflow already provisions; CI provisions the same major for the smoke test.
- Explicit `.ts` source specifiers with the existing extension-rewrite compiler option are the house style for the runtime (all other runtime modules already follow it), so the fix aligns the three outliers rather than changing build configuration.
- Declaration files keeping `.ts` relative specifiers is acceptable because both target compilers resolve them (verified during research); if a future compiler rejects them, that is a separate issue.
- The release workflow keeps its current ordering of irreversible steps; this feature only inserts a gate ahead of them.
- The Temporal polyfill / `lib: esnext` declaration conflict is pre-existing and out of scope; it is tracked separately if needed.
