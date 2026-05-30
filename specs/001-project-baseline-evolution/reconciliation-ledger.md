# Reconciliation Ledger

Disposition of every legacy `old-spec/` normative item, per
[contracts/reconciliation-ledger.contract.md](./contracts/reconciliation-ledger.contract.md).
Backs CR-001..CR-008, SC-004, SC-005.

**Parity gate (CR-004)**: `old-spec/` may be deleted only when this ledger has zero `Unprocessed`
rows and every retained item is present in the canonical source. **Status: NOT yet eligible**
(populated during US3).

## Document dispositions

| Legacy ref | Disposition | New location | Reason | Id preserved? |
| --- | --- | --- | --- | --- |
| `old-spec/01-product-vision.md` | Migrated | `baseline/00-overview.md` | Condensed into "What Metano is" | n/a |
| `old-spec/02-problem-scope-and-objectives.md` | Migrated | `baseline/00-overview.md` | Scope / not-in-scope folded in | n/a |
| `old-spec/03-stakeholders-and-use-cases.md` | Retained | `spec.md` (User Scenarios) | Personas covered by feature spec | n/a |
| `old-spec/04-functional-requirements.md` | Migrated | `baseline/feature-support-matrix.md` | FR → capability rows | `FR-001`–`FR-048` ✅ |
| `old-spec/05-non-functional-requirements.md` | Migrated | `baseline/00-overview.md` (+ roadmap) | Quality posture referenced | `NFR-001`–`NFR-020` ✅ |
| `old-spec/06-conceptual-architecture.md` | Migrated | `baseline/00-overview.md` | Frontend/IR/backend framing | n/a |
| `old-spec/07-glossary.md` | Retained | inline in overview/catalogs | Terms folded in context | n/a |
| `old-spec/08-feature-support-matrix.md` | Migrated | `baseline/feature-support-matrix.md` | + Code area/Test columns | n/a |
| `old-spec/09-attribute-catalog.md` | Migrated | `baseline/attribute-catalog.md` | Corrected to 27 attrs | attr names ✅ |
| `old-spec/10-diagnostic-catalog.md` | Migrated | `baseline/diagnostic-catalog.md` | Corrected to `MS0001`–`MS0025` | `MS0001`–`MS0025` ✅ |
| `old-spec/11-adr-cross-reference.md` | Retained | `docs/adr/` + matrix refs | ADR home unchanged | `ADR-00xx` ✅ |
| `old-spec/README.md` | Superseded | `baseline/README.md` | New entry point | n/a |

## Corrections applied (drift found vs. legacy)

| Item | Legacy said | Code truth | Where fixed |
| --- | --- | --- | --- |
| Attribute count | 26 (catalog) / 21 (CLAUDE.md) | **27** core + 3 TS-specific | `attribute-catalog.md` |
| Diagnostic range | `MS0022` (catalog) / `MS0024` (FR-039) / `MS0001-MS0008` (matrix) | **`MS0001`–`MS0025`** | `diagnostic-catalog.md` |
| Terminology | "transpilation target" | **backend**; "frontend" = source ingestion | `00-overview.md` |

## SSOT pointer updates (CR-007)

Documents that named `spec/` as the source of truth and must be repointed to the canonical speckit home:

| Document | Old reference | New reference | Updated? |
| --- | --- | --- | --- |
| `.specify/memory/constitution.md` | "FR/NFR in `spec/`" | `specs/001-project-baseline-evolution/` (speckit canonical) | ✅ |
| `CLAUDE.md` | "product specification under `spec/`" | `specs/001-project-baseline-evolution/` | ✅ |

> SSOT pointers repointed (T022). The migration parity gate below has passed and `old-spec/` has been
> deleted.

## Migration parity gate status (CR-004)

| Check | State |
| --- | --- |
| Every legacy doc has a disposition | ✅ (table above) |
| Stable identifiers preserved (`FR`/`NFR`/`MS`/`ADR`) | ✅ (see [.identifier-inventory.md](./baseline/.identifier-inventory.md)) |
| SSOT pointers repointed | ✅ |
| Architecture dispositions consistent with ADRs | ✅ (ADR-0001 / ADR-0013 unchanged; see overview) |
| **`old-spec/` eligible for deletion?** | ✅ **Deleted** — maintainer confirmed parity; `old-spec/` removed from the tree |

> The `old-spec/` references that remain in this ledger and in `.migration-index.md` are historical
> code-span text documenting the migration, not live file links — they record where each legacy
> document went after `old-spec/` was retired.
