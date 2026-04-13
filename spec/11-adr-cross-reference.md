# ADR Cross Reference

This appendix links the formal specification to the architectural decisions that
shape the current product.

## Mapping

| ADR | Title | Primary Spec Relationship |
| --- | --- | --- |
| `ADR-0001` | Target-agnostic core + per-target projects | Supports conceptual architecture and NFR maintainability requirements. |
| `ADR-0002` | Handler decomposition | Supports maintainability and transformation decomposition assumptions. |
| `ADR-0003` | Declarative BCL mappings | Supports FR-025, FR-026, FR-015, FR-016. |
| `ADR-0004` | Cross-project references via Roslyn | Supports FR-031 and cross-package scope. |
| `ADR-0005` | Inline wrapper branded types | Supports FR-018 and branded type terminology. |
| `ADR-0006` | Namespace-first barrel imports | Supports FR-028, FR-029, and output conventions. |
| `ADR-0007` | Output conventions | Supports NFR-001, NFR-018, and output structure assumptions. |
| `ADR-0008` | Overload dispatch | Supports FR-042 and overload semantics. |
| `ADR-0009` | Type guards | Supports FR-023. |
| `ADR-0010` | Diagnostics and stable codes | Supports FR-038, FR-039, and diagnostic catalog rules. |
| `ADR-0011` | `[EmitPackage]` as SSOT | Supports FR-030 and FR-031. |
| `ADR-0012` | LINQ eager wrapper strategy | Supports FR-017 and runtime support assumptions. |

## Guidance

- The specification describes **what** the product must do.
- ADRs explain **why** specific implementation directions were chosen.
- When the spec and an ADR appear to diverge, the spec should be updated if the
  product contract changed; otherwise the implementation may be drifting.
