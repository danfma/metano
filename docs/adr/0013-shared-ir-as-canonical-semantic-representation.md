# ADR-0013 — Shared IR as canonical semantic representation

**Status:** Accepted
**Date:** 2026-04-16

## Context

After [ADR-0001](0001-target-agnostic-core.md) split the core from the
TypeScript target and [ADR-0002](0002-handler-decomposition.md) broke the
TypeScript transformers into focused handlers, the next problem surfaced:
every target that wanted to consume Metano's output — TypeScript first,
Dart/Flutter second — re-walked the Roslyn semantic model itself. Record
shape detection, nullable lowering, overload folding, BCL mapping origins,
runtime helper discovery, the `[PlainObject]` / `[InlineWrapper]` / `[Emit]`
gates — each one lived inline in TS-specific handlers (`RecordClassTransformer`
at 1.529 lines, `ExpressionTransformer` + 13 child handlers, `TypeMapper`
with five `[ThreadStatic]` fields feeding 40+ call sites). A Dart target
written in the same shape would have had to re-derive the same Roslyn
heuristics, silently drifting from the TypeScript answer on any edge case.

Two paths forward:

- **Continue with one transformer tree per target.** Every new backend
  reproduces the semantic lowering from scratch over Roslyn.
- **Introduce a target-agnostic intermediate representation** between
  Roslyn and the target ASTs, owning every semantic decision once.

## Decision

Introduce a **shared intermediate representation** in
`src/Metano.Compiler/IR/` as the single canonical semantic layer between
the front-end (Roslyn today) and every target backend. The IR captures
modules, type declarations, members, constructors, expressions,
statements, type references, semantic annotations (`IrTypeSemantics`,
`IrMethodSemantics`, `IrNamedTypeSemantics`), and runtime helper facts
(`IrRuntimeRequirement`, `IrTypeOrigin`) as immutable records. Extractors
in `src/Metano.Compiler/Extraction/` convert Roslyn symbols + syntax into
IR; per-target bridges under `src/Metano.Compiler.{Target}/Bridge/` render
IR into target AST nodes.

Every IR addition is validated against the **anti-target-shaped checklist**:

| Rule | Good | Bad |
| --- | --- | --- |
| Semantic names | `IrPrimitive.Guid` | `IrPrimitive.UUID` |
| No import paths | `IrTypeOrigin("sample-todo", "Models")` | `IrTypeOrigin("sample-todo/src/models")` |
| No naming policy | `PropertyName = "FirstName"` | `PropertyName = "firstName"` |
| No emit flags | (absent) | `IsExported = true` |
| Semantic annotations | `IsRecord = true` | `HasEquals = true, HasHashCode = true` |

Pipeline realized today:

```
C# source → Roslyn compilation → IR extractors → Shared IR
                                                    │
                          ┌─────────────────────────┼─────────────────────────┐
                          ▼                         ▼                         ▼
                    TS bridges              Dart bridges             (future target)
                    ↓                       ↓
                    TS AST                  Dart AST
                    ↓                       ↓
                    .ts files               .dart files
```

The legacy transformers from [ADR-0002](0002-handler-decomposition.md) — the
TS-specific ones — are retired in favor of bridges over IR. The handler
decomposition pattern survives; the handlers just consume IR instead of
Roslyn directly.

## Consequences

- (+) **Multi-target lowering without drift.** A second target (Dart) lands
  with ~14 bridge files instead of cloning the entire TypeScript handler
  tree. Any regression in a semantic decision surfaces in both targets at
  once.
- (+) **Anti-target-shaped checklist is enforceable.** Every IR record is
  reviewed against the five rules above; Phase 7 (the Dart pilot) served
  as forcing function — every time the IR had a TS bias, the Dart bridge
  surfaced it.
- (+) **Bridges stay thin.** `IrToTsClassEmitter` walks
  `IrClassDeclaration.Members` and routes to per-shape bridges; no Roslyn
  symbol lookup in the hot path.
- (+) **Base for a second front-end.** The IR is Roslyn-free at the data
  level. A new `ISourceFrontend` (e.g., a future Metano-native language
  parser) can feed the same IR. See follow-up work on physically isolating
  the C# frontend (`Metano.Compiler.Frontend.CSharp`).
- (+) **Runtime helper requirements are data, not heuristics.**
  `IrRuntimeRequirement` produced by the scanner replaces the
  post-generation `ImportCollector` walk for IR-originated declarations.
- (−) **Extraction is single-pass.** Adding a C# construct means extending
  the extractor first, then every target bridge that cares. More surface
  area on day one.
- (−) **IR design overhead.** The checklist above is a permanent review
  tax — the temptation to ship a target-specific shortcut on the IR is
  real, especially under deadline pressure.
- (−) **File-count growth.** `IR/` + `Extraction/` add ~40 files to the
  core. Worth the cost given multi-target; would be overkill for a
  single-target transpiler.

## Alternatives considered

- **Keep handlers per target.** Rejected on the evidence above: Dart
  would have doubled the Roslyn-walking code and silently diverged on
  every edge case. The checklist-enforced IR is the cheaper long-term
  shape.
- **Target IR as "smaller TS AST".** Rejected. A TS-shaped IR would have
  forced the Dart backend to carry target-agnostic concepts through
  TS-flavored records; every Dart bridge would fight the anti-target-shaped
  rules. The IR has to own the semantic model, not the emission model.
- **Roslyn as the IR.** Rejected. Roslyn symbols carry binding information,
  nullability flow state, and SDK-specific types that no target needs; the
  backend would pay for Roslyn transitively, and a future non-C# front-end
  would have to synthesize fake `ISymbol` instances. The IR is a trimmed,
  backend-observable surface, not Roslyn wrapped.

## References

- `src/Metano.Compiler/IR/` — IR records (modules, declarations, members,
  expressions, statements, type references, semantic annotations, runtime
  requirements, diagnostics)
- `src/Metano.Compiler/Extraction/` — Roslyn → IR extractors
- `src/Metano.Compiler.TypeScript/Bridge/` — TS bridges and
  `IrToTsClassEmitter`
- `src/Metano.Compiler.Dart/Bridge/` — Dart bridges
- [ADR-0001](0001-target-agnostic-core.md) — the core/target split this
  decision builds on
- [ADR-0002](0002-handler-decomposition.md) — the handler pattern that
  survives, now consuming IR
- `docs/architecture.md` — pipeline reference, bridge catalog, anti-target-shaped rules
