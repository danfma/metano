# Feature Specification: Configurable Isolated Subpath-Import Alias for Generated Packages

**Feature Branch**: `004-configurable-import-alias`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "Add an opt-in, configurable, isolated subpath-import alias for Metano-generated TypeScript packages, so generated code can be emitted into a subfolder of an existing npm project without colliding with that project's own `#` subpath-imports alias. A new `--import-alias` CLI flag (and matching `MetanoImportAlias` MSBuild property) sets the alias; when set, generated internal imports and the package.json entries use the isolated alias key instead of the default `#`, leaving the host project's `#` untouched. When unset, behavior is unchanged (fully backward compatible)."

> Detailed problem statement, solution rationale, design decisions, edge cases,
> and a step-by-step implementation guide live in the design guide at
> `/Users/danfma/.claude/plans/vamos-adicionar-suporte-a-dynamic-hopcroft.md`.
> This specification captures the normative WHAT/WHY; the guide captures the HOW.

## User Scenarios & Testing *(mandatory)*

The "users" of this feature are developers who run Metano to generate TypeScript
and consume the output inside a TypeScript/npm project.

### User Story 1 - Emit into a subfolder of an existing project (Priority: P1)

A developer has an existing npm/TypeScript frontend whose `src/` directory is its
source root, already exposed through the `#/*` subpath-imports alias. They want to
generate a C# contracts library (e.g. `Abc.Contracts.Serialization`) into a
subfolder of that project, such as `src/abc/contracts`, and reference the
generated types from their hand-written code and from one generated type to
another (for example a serializer-configuration type that references sibling
generated types).

Today this breaks: Metano emits internal imports like
`import { MyType } from "#/my-type"` that resolve against the host project's own
`#/*` alias (→ the wrong path), and Metano also writes a conflicting `#`/`#/*`
entry into the host project's `package.json`.

With this feature, the developer configures an isolated alias (e.g. `contracts`)
for that Metano project. Generated internal imports then use the isolated key
(`#contracts/...`), the generated `package.json` entry maps that key to the actual
output subfolder, and the host project's own `#`/`#/*` alias is left untouched.

**Why this priority**: This is the core problem the feature exists to solve.
Without it, Metano cannot be used to generate code into a subfolder of an existing
project that already relies on `#`, which is a common, practical integration shape.

**Independent Test**: Configure the isolated alias on a Metano project whose output
directory is a nested subfolder of a host package that already defines `#/*`.
Transpile, then confirm the generated TypeScript compiles/resolves with no manual
edits and the host's pre-existing `#`/`#/*` entries are unchanged.

**Acceptance Scenarios**:

1. **Given** a host package that defines its own `#/*` alias and a Metano project
   whose output is a nested subfolder, **When** the developer configures an
   isolated alias and transpiles, **Then** every internal import in the generated
   code uses the isolated alias key and resolves to the correct file.
2. **Given** the same setup, **When** Metano updates the host `package.json`,
   **Then** only the isolated-alias entries are added (scoped to the output
   subfolder) and the host's existing `#`/`#/*` entries are neither added nor
   modified.
3. **Given** a generated type that lives in the project's root namespace,
   **When** another generated type imports it, **Then** the import uses the bare
   isolated-alias key (no trailing subpath).

### User Story 2 - Existing projects are unaffected (Priority: P1)

A developer who already uses Metano and does NOT configure an alias must observe
no change whatsoever in the generated output after this feature ships.

**Why this priority**: Backward compatibility is a release gate. Any change to the
default output would force regeneration of every existing consumer and invalidate
the project's golden test fixtures.

**Independent Test**: Transpile any existing sample with no alias configured and
diff the output against the pre-feature output — it must be identical.

**Acceptance Scenarios**:

1. **Given** a Metano project with no alias configured, **When** it is transpiled,
   **Then** the generated TypeScript and `package.json` are byte-identical to the
   current (default `#`/`#/*`) behavior.

### User Story 3 - Correct paths at any output depth (Priority: P2)

A developer emitting into a deeply nested output directory gets well-formed
`package.json` import/export path values regardless of nesting depth.

**Why this priority**: Nested output is exactly the scenario this feature targets,
and a malformed path (e.g. a doubled separator) would break resolution. It is a
correctness companion to Story 1, but narrower in impact.

**Independent Test**: Transpile into a nested output directory and inspect the
generated `package.json` import/export entries for malformed path segments.

**Acceptance Scenarios**:

1. **Given** a nested output directory, **When** Metano writes the `package.json`
   import/export entries, **Then** no path value contains a doubled path separator.

### Edge Cases

- The developer provides the alias value with or without a leading marker
  character — both forms yield the same alias key.
- The developer provides an empty or whitespace-only alias value — it is treated
  as "no alias configured" (default behavior).
- The host `package.json` already contains user-authored `#`/`#/*` entries and an
  alias is configured — the user entries are preserved AND the isolated-alias
  entries are added; nothing collides.
- Multiple Metano projects emit into the same TypeScript package — each project's
  alias is honored; choosing distinct aliases is the developer's responsibility
  (see Assumptions).
- A generated module participates in a circular reference while a custom alias is
  in effect — the cycle is still detected and reported as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Metano MUST provide an opt-in configuration option that sets a
  custom, isolated subpath-import alias for internal (same-project) imports in the
  generated TypeScript.
- **FR-002**: When an alias is configured, every internal import in the generated
  code MUST use the configured alias key (e.g. `#contracts/...`, and the bare
  alias key for root-namespace types) instead of the default `#`.
- **FR-003**: When an alias is configured, the generated/updated `package.json`
  MUST contain only the alias-scoped subpath-import entries, scoped to the output
  directory, and MUST NOT add or modify the default `#`/`#/*` entries.
- **FR-004**: When no alias is configured, the generated TypeScript and
  `package.json` MUST be identical to the current default behavior, preserving full
  backward compatibility.
- **FR-005**: The alias MUST be configurable through both existing configuration
  surfaces (the command-line interface and the build-system integration) on a
  per-project basis.
- **FR-006**: Configuring an alias MUST NOT change how imports of types from
  externally referenced packages are emitted; those continue to use the referenced
  package's published package name.
- **FR-007**: The alias value MUST be normalized: a leading marker character, if
  supplied, is accepted equivalently to its absence, and an empty or
  whitespace-only value MUST be treated as "no alias configured".
- **FR-008**: Subpath-import path values written to `package.json` MUST be free of
  malformed segments (e.g. doubled path separators) at any output-directory depth.
- **FR-009**: Internal cyclic-reference detection and reporting MUST continue to
  function correctly when a custom alias is in effect.
- **FR-010**: Changing the alias configuration MUST invalidate any cached
  generation output so that stale output produced under a different alias is never
  reused.

### Key Entities

- **Subpath-import alias**: The configurable key under which a project's internal
  generated modules are exposed and imported (default: `#`). When set, it is an
  isolated key distinct from the host project's own `#`.
- **Generated package.json imports map**: The set of subpath-import entries Metano
  contributes to the consuming package, mapping the alias key to the build and
  source locations of the generated code.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can emit generated code into a subfolder of an existing
  npm project and the generated TypeScript resolves/builds with zero manual edits
  to import statements.
- **SC-002**: For projects that do not configure an alias, generated output is
  unchanged — zero differences across all existing samples and golden outputs.
- **SC-003**: After Metano runs against a host project that already defines `#`,
  the host's pre-existing `#`/`#/*` entries show zero modifications.
- **SC-004**: Generated `package.json` import/export path values contain zero
  malformed (doubled-separator) segments at any tested output depth.
- **SC-005**: The alias is configurable with a single setting in either the CLI or
  the project file, requiring no manual post-processing of the generated artifacts.

## Assumptions

- The audience is developers integrating Metano-generated TypeScript into existing
  npm/TypeScript projects; the alias is a build-time configuration, not an
  end-user-facing runtime concern.
- The feature is opt-in; the default behavior (key `#`) is unchanged so existing
  consumers and the project's golden test fixtures are not disturbed.
- When several Metano projects emit into the same TypeScript package, each project
  is responsible for choosing a distinct alias. Automatic detection of conflicting
  aliases across projects is out of scope for this version (documented as a known
  limitation; a future diagnostic may be added).
- No new configuration-file format (e.g. `metano.json`) is introduced; the existing
  command-line and build-system configuration surfaces are reused. A future config
  file may reuse the same setting.
- Cross-package export/import resolution for referenced packages already works for
  nested output directories and is not changed by this feature.
