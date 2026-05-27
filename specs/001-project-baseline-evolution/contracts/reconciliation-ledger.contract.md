# Contract: Reconciliation Ledger

**Artifact**: `reconciliation-ledger.md` · **Backs**: CR-001..CR-008, SC-004, SC-005

## Required table shape

```md
| Legacy ref | Disposition | New location | Reason | Id preserved? |
| --- | --- | --- | --- | --- |
```

## Rules

- One row per legacy `old-spec/` normative item (every `FR-###`, `NFR-###`, `MS####`, attribute, and any
  free-standing normative statement).
- `Disposition` ∈ {`Migrated`, `Retained`, `Retired`}.
- `Disposition = Retired` (or superseded) ⇒ `Reason` MUST be non-empty (CR-001).
- `Disposition ≠ Retired` ⇒ `New location` MUST point at the canonical anchor.
- `Id preserved?` MUST be `true` for every `FR/NFR/MS` identifier (CR-006).
- Architecture-affecting dispositions MUST cite the relevant ADR and stay consistent with it (CR-008).

## Parity gate (CR-004)

`old-spec/` may be deleted only when the ledger has **zero** `Unprocessed` rows and every retained item is
present in the canonical source. Until then `old-spec/` stays in-tree.

## Acceptance

- 100% of legacy normative items have a disposition (SC-004).
- Every legacy stable identifier resolves to exactly one canonical definition (SC-005, INV-3).
- No normative item exists in two places with conflicting wording (CR-005, INV-2).

## Companion: SSOT pointer updates (CR-007)

A short section in the ledger lists each document that named `spec/` as the source of truth
(`.specify/memory/constitution.md`, `CLAUDE.md`, any others) with old → new reference and an `Updated?` flag.
All flags MUST be `true` before feature completion.
