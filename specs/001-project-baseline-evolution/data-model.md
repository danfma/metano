# Phase 1 Data Model: Documentation Artifacts

This feature emits documents, not runtime data. The "entities" are the document artifacts and their
schemas. Each maps to spec entities and requirements.

## Entity: Capability Baseline Entry

One row per product capability. Lives in `baseline/feature-support-matrix.md`.

| Field | Type | Rules |
|-------|------|-------|
| Area | enum(text) | Semantic group (e.g. Types, Enums, Collections, Output, Diagnostics). Required. |
| Feature | text | Capability name. Required, unique within Area. |
| Backend | enum | `TypeScript` \| `Dart` \| `All` — which backend the row describes. Default `TypeScript` (reference backend). |
| Status | enum | `Implemented` \| `Partial` \| `Planned`. Required. |
| Code area | path/ref | Source location backing the capability. **Required when Status = Implemented** (BR-005, SC-001). |
| Test | path/ref | At least one test proving it. **Required when Status = Implemented**. |
| Constraints | text | Enumerated unsupported sub-cases. **Required when Status = Partial** (BR-004). |

- Maps to: BR-003, BR-004, BR-005. State: a row's Status may change only with matching Code area/Test/Constraints.

## Entity: Roadmap Thrust

One per evolution direction. Lives in `roadmap/00-roadmap.md`.

| Field | Type | Rules |
|-------|------|-------|
| ID | text | `RT-NN`. Required, unique. |
| Title | text | Required. |
| Priority | int (rank) | Unique rank. `RT` with rank 1 MUST be SPI hardening (RR-002). |
| Outcome | text | Intended end state. Required. |
| Traceability | ref[] | Links to baseline gap(s) and/or vision statement. Required (RR-001). |
| Dependencies | ID[] | Other thrust IDs this depends on (RR-005). May be empty. |
| Scope note | text | What is in/out for the thrust's future spec. Required. |

- Seeded thrusts: RT-01 Harden multi-target SPI (rank 1), RT-02 Dart backend to parity, RT-03 Frontend-
  extensibility posture. Maps to: RR-001..RR-006.

## Entity: Reconciliation Ledger Item

One per legacy `old-spec/` normative item. Lives in `reconciliation-ledger.md`.

| Field | Type | Rules |
|-------|------|-------|
| Legacy ref | text | Stable id or doc+section (e.g. `FR-030`, `old-spec/06 §5`). Required, unique. |
| Disposition | enum | `Migrated` \| `Retained` \| `Retired`. Required. |
| New location | path/anchor | Where it now lives. Required unless `Retired`. |
| Reason | text | **Required when Disposition = Retired or superseded** (CR-001). |
| Identifier preserved? | bool | Must be `true` for every `FR/NFR/MS` id (CR-006). |

- Maps to: CR-001, CR-005, CR-006. Parity gate (CR-004): `old-spec/` deletable only when every item is
  `Migrated` or `Retained` or `Retired-with-reason` — none `Unprocessed`.

## Entity: SSOT Pointer Update

Tracks docs that name `spec/` as the source of truth and must be repointed (CR-007).

| Field | Type | Rules |
|-------|------|-------|
| Document | path | e.g. `.specify/memory/constitution.md`, `CLAUDE.md`. Required. |
| Old reference | text | The `spec/` mention. Required. |
| New reference | text | The canonical speckit location. Required. |
| Updated? | bool | Must be `true` before feature completion. |

- Maps to: CR-007.

## Cross-entity invariants

- INV-1 (SC-001): every `Implemented` Capability Baseline Entry has non-empty `Code area` AND `Test`.
- INV-2 (CR-005): no normative statement appears in both `old-spec/` and the canonical source with
  conflicting wording.
- INV-3 (CR-006 / SC-005): every legacy `FR/NFR/MS` identifier resolves to exactly one canonical definition.
- INV-4 (RR-002 / SC-003): the rank-1 Roadmap Thrust is SPI hardening with an additive-only outcome.
