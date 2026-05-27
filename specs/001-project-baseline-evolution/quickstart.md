# Quickstart: Navigating & Validating the Baseline

Audience: maintainers, adopters, AI agents. Goal: answer "what does Metano do today, and what's next?"
in under 15 minutes (SC-002) — and validate the migration is sound.

## Read order

1. `baseline/00-overview.md` — frontend (C#/Roslyn) → IR → backends (TypeScript, Dart) framing.
2. `baseline/feature-support-matrix.md` — every capability with Status + Code area + Test.
3. `roadmap/00-roadmap.md` — ranked thrusts; #1 is multi-target SPI hardening.
4. `reconciliation-ledger.md` — where each legacy item went; SSOT pointer updates.

## "What does it do today?" (60-second answer)

- Frontend: **C#/Roslyn** (only current source language).
- Backends: **TypeScript** (Implemented, reference) and **Dart/Flutter** (Partial — see matrix gaps).
- Customization via ~21 attributes; diagnostics `MS0001`–`MS0025`; cross-package npm output.

## "What's next?" (60-second answer)

- **RT-01 (P1)** Harden the multi-target SPI: stabilize IR + backend port → new backend = additive only.
- **RT-02** Dart backend to parity (close enumerated gaps).
- **RT-03** Frontend-extensibility posture (keep IR frontend-agnostic; new source languages deferred).

## Validate the migration

```sh
# 1. No dangling stable identifiers in the canonical source.
grep -REho 'FR-[0-9]{3}|NFR-[0-9]{3}|MS[0-9]{4}' specs/001-project-baseline-evolution/baseline \
  | sort -u   # cross-check each id resolves to a definition

# 2. old-spec/ still present until parity gate passes (CR-004).
ls old-spec/ >/dev/null && echo "old-spec present (expected until parity)"

# 3. Every 'Implemented' matrix row names a Code area + Test (review-confirmed).
```

Pass criteria: SC-001 (Implemented rows traceable), SC-004 (every legacy item dispositioned),
SC-005 (no dangling ids), SC-003 (rank-1 thrust = SPI hardening).
