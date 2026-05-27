# Contract: Roadmap Thrust Entry

**Artifact**: `roadmap/00-roadmap.md` · **Backs**: RR-001..RR-006, SC-003, SC-006

## Required entry shape (one per thrust)

```md
### RT-NN — <Title> (Priority: <rank>)

- **Outcome**: <intended end state, testable>
- **Traceability**: <baseline gap id / vision ref>
- **Dependencies**: <RT-ids | none>
- **Scope note**: <in / out for the future spec>
```

## Rules

- `Priority` ranks are unique integers; **rank 1 MUST be the SPI-hardening thrust** (RR-002) with an
  additive-only outcome: "adding a new backend touches only the new backend's artifacts; zero edits to core
  or existing backends."
- Seeded set (minimum): `RT-01` Harden multi-target SPI (rank 1), `RT-02` Dart backend to parity,
  `RT-03` Frontend-extensibility posture (IR kept frontend-agnostic; new frontends deferred).
- No entry contains implementation steps (RR-006) — outcome + scope only.
- Each entry MUST be self-contained enough to seed `/speckit-specify` without extra context (SC-006).

## Acceptance

- A reader names the #1 priority and its additive-only target from the roadmap alone (SC-003).
- Every thrust links back to a baseline gap or a vision statement (RR-001).
