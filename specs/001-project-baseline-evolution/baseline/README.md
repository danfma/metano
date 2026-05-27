# Metano Baseline — Canonical Specification

> **Single source of truth** for what Metano does today. Migrated from the legacy `spec/` (now
> `old-spec/`) into speckit format. For the evolution direction see
> [../roadmap/00-roadmap.md](../roadmap/00-roadmap.md).

## Read order (≤15 minutes)

1. **[00-overview.md](./00-overview.md)** — what Metano is; frontend (C#/Roslyn) → IR → backends
   (TypeScript, Dart) framing; the pipeline.
2. **[feature-support-matrix.md](./feature-support-matrix.md)** — every capability with status +
   `Code area` + `Test` (traceability).
3. **[attribute-catalog.md](./attribute-catalog.md)** — the 27 `Metano.Annotations` attributes (+ 3
   TypeScript-backend-specific).
4. **[diagnostic-catalog.md](./diagnostic-catalog.md)** — stable codes `MS0001`–`MS0025`.
5. **[../roadmap/00-roadmap.md](../roadmap/00-roadmap.md)** — ranked evolution thrusts (#1 = harden the
   multi-target SPI).
6. **[../reconciliation-ledger.md](../reconciliation-ledger.md)** — where every legacy item went + SSOT
   pointer updates.

## 60-second summary

- **Frontend**: C#/Roslyn (the only current source language).
- **IR**: a shared, language-neutral semantic model — the contract the multi-target story centers on.
- **Backends**: TypeScript (Implemented, reference) and Dart/Flutter (Partial — see matrix gaps).
- **Customization**: ~27 attributes; diagnostics `MS0001`–`MS0025`; cross-package npm output.
- **Next**: RT-01 harden the SPI → RT-02 Dart parity → RT-03 frontend-extensibility posture.

## Provenance

- Authoritative since the `001-project-baseline-evolution` reconciliation (constitution + `CLAUDE.md`
  point here).
- ADRs remain in `docs/adr/` (24 ADRs); rationale for the IR is ADR-0013, target-agnostic core ADR-0001.
- Stable identifiers (`FR-###`, `NFR-###`, `MS####`, `ADR-00xx`) preserved — see
  [.identifier-inventory.md](./.identifier-inventory.md).
