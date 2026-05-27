# Contract: Feature-Support Matrix (Baseline)

**Artifact**: `baseline/feature-support-matrix.md` · **Backs**: BR-003, BR-004, BR-005, SC-001

## Required table shape

```md
| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
```

## Cell rules

- `Backend` ∈ {`TypeScript`, `Dart`, `All`}. A target-specific row names its backend; shared rows use `All`.
- `Status` ∈ {`Implemented`, `Partial`, `Planned`}.
- `Status = Implemented` ⇒ `Code area` and `Test` MUST be non-empty and resolvable (path or symbol).
- `Status = Partial` ⇒ `Constraints` MUST enumerate the unsupported sub-cases (no vague "limited support").
- `Status = Planned` ⇒ `Code area`/`Test` empty; `Constraints` may hold the planned-gap note.

## Acceptance

- Pick any `Implemented` row → its `Code area` exists in the tree and its `Test` runs and asserts the behavior.
- No row mixes two backends in one line (split per backend instead).
- A separate **Target Support** table lists backends (TypeScript Implemented, Dart Partial) and the frontend
  (C#/Roslyn) per BR-001..BR-003.
