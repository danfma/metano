# Contract: Dart mapping for overload factories and direct call sites

**Normative for**: 013 FR-022 (DECIDED-14). The Dart target is a shape-only prototype; this
contract fixes the **names and the call-site spelling**. The emission lands with Dart parity
(roadmap RT-02); nothing in `targets/flutter` changes with this feature.

| C# | Dart |
|---|---|
| `Dimension(int size)` / `Dimension(int width, int height)` (dispatched) | named constructors `Dimension.fromSize(this.size)` and `Dimension.fromWidthHeight(this.width, this.height)` — the core candidates, Dart's native syntax |
| the selector symbol | **not emitted** — Dart named constructors already address one constructor |
| a method fast path | the same candidate as TypeScript (`addXY`, `fInt32`, `nowDefault`, `kNumericsDecimal`), root-anchored (FR-025) |
| `new Money(19.9m)` (direct call site) | `Money.fromAmount(…)` (the argument is the Dart lowering of the `decimal` literal) |
| `p.Add(point)` (direct call site) | `p.addP(point)` |
| a hand-written Dart `Dimension(3, 4)` | n/a — the prototype keeps today's constructor shape; dispatch for hand-written Dart is RT-02's |

**Claims are Dart's own (DECIDED-18).** Dart has one member namespace per class for instance
members, static members and named constructors, so the Dart bridge runs the claim pass of
naming.md over one table where TypeScript keeps static and instance names apart. An instance fast
path `fromCents` beside a factory `fromCents` is legal TypeScript but a Dart conflict; the Dart
bridge renames it (`fromCents$`) and reports `MS0067`. The two targets may therefore spell the
same overload differently after claims; the core candidates are identical.
