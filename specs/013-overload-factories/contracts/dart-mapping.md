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

## As landed (slice 013g)

- **Declarations.** `IrToDartClassBridge` emits one named constructor per overload of a dispatched
  set, in declaration order, with no unnamed constructor; a `: this(...)` chain is a redirecting
  constructor (`Point.fromV(int v) : this.fromXY(v, v);`, named chain arguments placed by
  position, skipped ones passing their default, promoted parameters made plain because Dart
  rejects `this.x` in a redirect); a chain with a body of its own keeps the body and reports
  `MS0001` (Dart redirects have none). A record's `copyWith` rebuilds through the primary's
  named constructor. Method, interface and containerless-module groups emit one fast path per
  overload (`addXY`, `areaSide`, `twiceText`), with no dispatcher; an override or
  implementation takes its root's name (`super.speakN(n)`). An overloaded **operator** group
  keeps its single `operator` declaration and the `MS0001` warning — Dart declares one
  `operator +` per class — and an operator application keeps the source operator
  (`IrExtractionContext.EmitsOperatorFastPaths` is false for Dart). `[PlainObject]` and branded
  types keep today's single primary member (DECIDED-10).
- **Claims.** `DartClassClaimPass` claims, in one table per class: user members, the named
  constructors, the anchored fast paths (their root's names), then the own fast paths, which
  also claim against the inherited members of every visibility; `DartFlatClaimPass` claims an
  interface's members and a containerless module's functions (per file: a Dart library is the
  module's own scope). `DartOverloadClaimsRegistry` caches the result per type, computed from
  its shape IR.
- **Call sites.** The core binds Dart call sites as it binds TypeScript's: `new T(...)` through
  `EmitsConstructorSelectors` (answered by `DartTypeShapes.EmitsNamedConstructors`) and every
  method, `base.M`, method-group, extension and module-function call through
  `DispatchedMethodCalls`; `DartIrRewriter` renames them to the claimed names
  (`Base.fromAB(1, 5)` with the omitted default explicit). A generic construction names the
  named constructor without the type arguments, which Dart infers from the arguments. A
  construction whose named arguments evaluate out of parameter order
  (`IrSelectedOverload.EvaluationOrder`) evaluates them in parameter order in Dart. The prototype
  still emits no `super(...)` initializer for a derived constructor (RT-02), so `BaseOverload`
  is unused there.
- **`[Name]`.** Pins the suffix as in TypeScript, with the same `MS0003` reports; a method's
  whole-member `[Name]` is spelled at its call sites too (`a?.isSame(b)`), as its declaration
  always was. `IrParameterNaming` reads the target's `[Name]`, so a parameter shadowing a
  Dart-renamed flattened function takes `$`.
