# Metano Evolution Roadmap

Prioritized, future-facing evolution thrusts. Per
[contracts/roadmap-thrust.contract.md](../contracts/roadmap-thrust.contract.md). Each thrust is a seed
for its own future `/speckit-specify`; **this document commits no implementation** (RR-006).

> "Parity" here means **backend parity** (a backend matching another backend's supported surface). It is
> distinct from the **migration parity** gate that governs deleting `old-spec/` (see reconciliation ledger).

## Priority ranking

| Rank | ID | Thrust |
| --- | --- | --- |
| 1 | `RT-01` | Harden the multi-target SPI |
| 2 | `RT-02` | Dart backend to parity |
| 3 | `RT-03` | Frontend-extensibility posture |

---

### RT-01 — Harden the multi-target SPI (Priority: 1)

- **Outcome**: The IR (`src/Metano.Compiler/IR/*`) and the backend port (`ITranspilerTarget`) are a
  stable, documented contract such that **adding a new backend touches only the new backend's own
  artifacts — zero edits to the core or to existing backends**. "Additive-only" is the acceptance bar.
- **Traceability**: NFR-010 / NFR-011 (separation of responsibilities; evolvable by area); ADR-0001
  (target-agnostic core); ADR-0013 (shared IR as canonical representation). Baseline signal: Dart is
  still `Partial`, so the additive-only property is not yet proven in practice.
- **Dependencies**: none (foundational — the other thrusts build on it).
- **Scope note**: *In* — document and stabilize the `ITranspilerTarget` surface; audit IR completeness
  for backend needs; add a conformance harness (e.g. a minimal reference backend) that proves
  additive-only. *Out* — shipping a new production backend; changing source-language support.

### RT-02 — Dart backend to parity (Priority: 2)

- **Outcome**: The Dart/Flutter backend matches the TypeScript backend's supported surface (backend
  parity) for the shared IR-expressible feature set.
- **Traceability**: feature-support matrix rows where Dart = `Partial`. Known gaps from the baseline:
  classic extension methods, `[ModuleEntryPoint]` body lowering, and JSON serializer-context emission
  (plus any further gaps surfaced while filling the matrix).
- **Dependencies**: `RT-01` (a stable SPI makes parity work additive rather than core-coupled).
- **Scope note**: *In* — close the enumerated Dart gaps; bring Dart samples under `targets/flutter/*`
  to the TS samples' coverage. *Out* — Dart-only features with no C#/IR source; non-shared surface.

### RT-03 — Frontend-extensibility posture (Priority: 3)

- **Outcome**: The IR is provably **frontend-agnostic** — additional source languages (frontends beyond
  C#/Roslyn) remain conceivable without an IR rewrite. No new frontend is implemented or committed.
- **Traceability**: ADR-0013 ("foundation for additional source frontends"); product vision
  ("…even frontends in mind, for the future").
- **Dependencies**: `RT-01` (the same IR-contract work surfaces any C#-specific leakage).
- **Scope note**: *In* — document IR frontend-neutrality invariants; identify and flag C#-isms that
  have leaked into the IR; record additional source languages as a deferred direction. *Out* —
  building any non-C# frontend; promising a timeline.

---

## How to pick up a thrust

Each entry above is self-contained (RR-005, SC-006): start a new `/speckit-specify` from its Outcome +
Scope note. Recommended order follows the rank (RT-01 first — it de-risks RT-02 and RT-03).
