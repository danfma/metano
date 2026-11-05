# Contract: Dart mapping for overload factories and direct call sites

**Normative for**: 013 FR-022 (DECIDED-14). The Dart target is a shape-only prototype; this
contract fixes the **names and the call-site spelling**. The emission lands with Dart parity
(roadmap RT-02); nothing in `targets/flutter` changes with this feature.

| C# | Dart |
|---|---|
| `Dimension(int size)` / `Dimension(int width, int height)` (dispatched) | named constructors `Dimension.fromSize(this.size)` and `Dimension.fromWidthHeight(this.width, this.height)` — the policy's suffixes, Dart's native named-constructor syntax |
| the selector symbol | **not emitted** — Dart has no `unique symbol` analogue and its named constructors already address one constructor |
| a method fast path | the same public method name as TypeScript (`addXY`, `fInt32`, `nowDefault`, `kNumericsDecimal`) |
| `new Money(19.9m)` (direct call site) | `Money.fromAmount(19.9)` |
| `p.Add(point)` (direct call site) | `p.addP(point)` |
| a hand-written Dart `Dimension(3, 4)` | n/a — the Dart prototype keeps today's constructor shape; the dispatch/fallback question is RT-02's |

Why the names can be shared: the policy lives in the core (DECIDED-1), so both targets render the
same `IrOverloadName`. Why factories map cleanly: a Dart named constructor is exactly a per-
overload constructor entry point — the same concept the C# factory expresses.
