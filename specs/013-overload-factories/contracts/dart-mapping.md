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
naming.md over one table where TypeScript keeps static and instance names apart. In that table a
named constructor conflicts with another named constructor or a static member, not with an
instance member (`dart analyze`: `conflicting_constructor_and_static_member`); every other member
conflicts with any member of its name. A named constructor `fromCents` beside a static
`FromCents()` is renamed (`fromCents$`) with `MS0067`, as TypeScript renames its factory; an
instance `fromSize()` beside `Taken.fromSize` keeps both names; an instance fast path `addX`
beside a static user member `AddX()` is renamed in Dart only. The two targets may therefore spell
the same overload differently after claims; the core candidates are identical.

## As landed (slice 013g)

- **Declarations.** `IrToDartClassBridge` emits one named constructor per overload of a dispatched
  set, in declaration order, with no unnamed constructor; a `: this(...)` chain is a redirecting
  constructor to the named constructor of the overload it chains to (the chain target carries its
  `DeclarationOrdinal`: `Opt.fromS(String s) : this.fromBig(7);`), named chain arguments placed
  by position, skipped ones passing their default, promoted parameters made plain because Dart
  rejects `this.x` in a redirect. A chain with a body of its own keeps the body without the chain
  and reports `MS0001` at the constructor (Dart redirects have no body; the TypeScript bridge's
  chain inlining is not ported). A record's `copyWith` rebuilds through the primary's named
  constructor. Method, interface and containerless-module groups emit one fast path per
  overload (`addXY`, `areaSide`, `twiceText`), with no dispatcher; an override or
  implementation takes its root's name (`super.speakN(n)`). An overloaded **operator** group
  keeps its single `operator` declaration and the `MS0001` warning — Dart declares one
  `operator +` per class — and an operator application keeps the source operator (Dart leaves the
  opt-in `IrExtractionContext.EmitsOperatorFastPaths` unset). `[PlainObject]` and branded types
  keep today's single primary member with `MS0001` (DECIDED-10), declared under the name its
  call sites spell: the core pins a branded overload's `[Name]` as a suffix and renames a
  `[PlainObject]` one, and a conversion operator's `[Name]` names it
  (`DartClassMemberNames.MethodNameOf`). Whether a type names its overload members and whether a
  `new` binds its named constructor come from one per-symbol shape (`DartTypeShapes.ShapeOf`), so
  an `[Import]` class Dart emits declares and constructs through named constructors alike.
- **Pre-existing Dart gaps that touch overloads:** a base whose unnamed constructor has only
  optional parameters lets a derived constructor compile and run it with its defaults, silently
  (#572); a `params` argument is passed element by element (#585); out-of-order named arguments
  of a non-overloaded call print as named arguments (#586); a generic method call drops its type
  arguments (#587).
- **Claims.** `DartClassClaimPass` claims, in one table per class: user members, the named
  constructors, the anchored fast paths (their root's names), then the own fast paths, which
  also claim against the inherited members of every visibility; `DartFlatClaimPass` claims an
  interface's members and a containerless module's functions (per file: a Dart library is the
  module's own scope). `DartOverloadClaimsRegistry` caches the result per type, computed from
  its shape IR; a declaration whose claim is missing is a broken invariant and throws.
- **Call sites.** The core binds Dart call sites as it binds TypeScript's: `new T(...)` through
  `EmitsConstructorSelectors` (answered by `DartTypeShapes.EmitsNamedConstructors`) and every
  method, `base.M`, method-group, extension and module-function call through
  `DispatchedMethodCalls`; `DartIrRewriter` renames them to the claimed names
  (`Base.fromAB(1, 5)` with the omitted default explicit). A construction keeps its type
  arguments before the constructor's name (`Gen<Object>.fromValue(5)`, the Dart-only
  `DartNamedConstruction` node). A construction or call whose named arguments evaluate out of
  parameter order (`IrSelectedOverload.EvaluationOrder`) binds them, and an impure receiver
  first, in the source's order through immediately invoked closures
  (`(($arg1, $arg0) => c.subXY($arg0, $arg1))(next(), next())`: one closure whose arguments Dart
  evaluates left to right, a receiver that is not a plain value bound first as `$recv` — a member
  access an argument may reassign included; an `await` among the arguments stays outside the
  closure, in the enclosing async function; `DartEvaluationOrder`).
  The prototype still emits no `super(...)` initializer for a derived constructor (RT-02, #572),
  so `BaseOverload` is unused there.
- **`[Name]`.** Pins the suffix as in TypeScript, with the same `MS0003` reports; a method's
  whole-member `[Name]` is spelled at its call sites too (`a?.isSame(b)`), as its declaration
  always was. `IrParameterNaming` reads the target's `[Name]`, so a parameter shadowing a
  Dart-renamed flattened function takes `$`.
