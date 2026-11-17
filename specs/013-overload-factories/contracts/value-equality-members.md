# Contract: Synthesized value-equality members in overload groups

**Normative for**: 013 FR-038–FR-045, DECIDED-33–DECIDED-35; baseline FR-060.
**Status**: design, for review before implementation (wave 3.0, round 6). It replaces the
round-3 forced rename (`ValueEqualityMemberNames`, `MethodOverloadGroup.YieldsToValueEquality`)
and the round-4 TypeScript-only join, which could not anchor overrides or inherit dispatchers.

## Problem

A record or a plain struct emitted as a class carries value-equality members C# synthesizes or
inherits (baseline FR-060): `Equals(object)`, `GetHashCode()` and, for a record, the copy behind a
`with` expression. A target emits them under names its runtime calls: TypeScript's `valueEquals`,
`HashSet` and `Dictionary` call `equals(other)` and `hashCode()`, and the `with` lowering calls
`with(overrides)`. A user method can be spelled the same way in the target (`Equals(RTol other,
double tol)` → `equals`, `HashCode(int seed)` → `hashCode`, a record's `With(int a)` → `with`), and
so can a member that implements an interface (`IEqualityComparer<T>.Equals(T, T)`). Today one of
the two members is silently lost or replaced.

The fix lets the synthesized member **join** the user group that shares its emitted name as one
more overload. The spec-013 machinery then does what it does for any overload group: names, root
anchoring across a hierarchy, inherited dispatchers forwarding to `super`, `override` flags,
direct call sites and the claim pass.

## 1. The group member: declared or synthesized (FR-038)

Core groups `IMethodSymbol`s today (`MethodGroupMember(IMethodSymbol Symbol, int Ordinal,
MethodAnchor? Anchor)`). Only one synthesized member has a Roslyn symbol of the needed shape (a
record's implicit `Equals(object)`); a struct's lives on `System.ValueType`, a record's
`GetHashCode()` groups under another C# name, and the record copy is `<Clone>$()`, which takes no
property bag. So a group member becomes a small closed hierarchy:

```csharp
internal abstract record GroupMember(int Ordinal);

// A method the type declares, as today.
internal sealed record DeclaredMember(IMethodSymbol Symbol, int Ordinal, MethodAnchor? Anchor)
    : GroupMember(Ordinal);

// A value-equality member the type synthesizes, described by data.
internal sealed record SynthesizedMember(
    SynthesizedValueMember Kind,      // Equals | HashCode | Copy
    INamedTypeSymbol Owner,           // the record or struct
    int Ordinal,                      // after every declared member (identity stays stable)
    SynthesizedAnchor? Anchor         // the same Kind in the nearest emitted base that joined it
) : GroupMember(Ordinal);
```

`SynthesizedValueMember` is a target-neutral enum of C# facts. The member's shape is fixed by its
kind and never read from a symbol:

| Kind | C# meaning | Parameters (dispatch shape) | Return | Suffix |
|---|---|---|---|---|
| `Equals` | `Equals(object?)` / `ValueType.Equals` | `other: object` (no runtime check) | `bool` | the contract's pinned suffix |
| `HashCode` | `GetHashCode()` | none | `int` | the contract's pinned suffix |
| `Copy` | the record copy of `with` | `overrides: object`, optional (no runtime check) | the record type | the contract's pinned suffix |

The IR carries it as an `IrMethodDeclaration` with `Semantics.IsSynthesized`, a new
`IrSynthesizedValueMember? SynthesizedKind` and `Body: null`; a target supplies the body it already
synthesizes (TypeScript: `IrToTsRecordSynthesisBridge`).

### Which symbol reads become member reads

| File | Today | After |
|---|---|---|
| `MethodOverloadGroups.cs` (11) | groups, anchors and names `MethodGroupMember.Symbol` | `GroupMember`; `GroupsNamed` inserts the `SynthesizedMember` (§ 3); `SuffixesOf` returns `IReadOnlyDictionary<GroupMember, string>`; `AnchorOf` for a synthesized member is `SynthesizedAnchorOf` (§ 4); `RefOf(GroupMember)` beside `RefOf(IMethodSymbol)`; new `SynthesizedOf(type, kind, policy)` |
| `OverloadGroupNaming.cs` (1) | `PinnedOverload(IMethodSymbol Symbol, OverloadSuffixPin? Pin)` | `PinnedOverload(GroupMember Member, OverloadSuffixPin? Pin)`; a synthesized member is always pinned to the contract suffix, so stage 1 never reads its parameters |
| `IrClassExtractor.cs` (6) | `ExtractNamedOverloads` keyed by `IMethodSymbol`; `AnchorOf(MethodGroupMember)` | keyed by `GroupMember`; builds the synthesized member's IR from its kind; `AnchorOf` maps a `SynthesizedAnchor` to `IrOverloadAnchor` |
| `DispatchedMethodCalls.cs` (6) | `RefOf(IMethodSymbol)` for a call | unchanged for declared members; adds the bindings of § 5 (implicit `Equals` / `GetHashCode`, `with` expressions) to the synthesized member's ref |
| `IrExpressionExtractor.cs` (4) | `base.M(...)` and `IsEmittedBase` | `base.Equals(x)` / `base.GetHashCode()` inside a joined type resolve to the base's synthesized ref (§ 4) |
| `MultiplyConstructedInterfaces.cs` (2), `OverloadEmittedName.cs` (1) | interface roots, `[Name]` pins | unchanged: they only see declared members |

## 2. The target contract (FR-039)

The target tells core how it spells methods and which names its synthesized members take. It
reaches the groups through the existing target seam: `MethodOverloadGroups.Of` and its callers
take an `OverloadGroupingPolicy` (today they take `TargetLanguage? target`), built from
`IrExtractionContext`, which already carries per-target capabilities (`EmitsOperatorFastPaths`,
`EmitsConstructorSelectors`).

```csharp
public sealed record OverloadGroupingPolicy(
    TargetLanguage? Target,
    SynthesizedMemberNames? ValueEqualityNames);   // null: nothing joins (today's output)

public sealed record SynthesizedMemberNames(
    IReadOnlyDictionary<SynthesizedValueMember, string> EmittedNames,
    string FastPathSuffix,                          // e.g. "ByValue"
    Func<string, string> MethodSpelling);           // C# method name -> target spelling
```

- **TypeScript** supplies `{ Equals: "equals", HashCode: "hashCode", Copy: "with" }`, the suffix
  `ByValue` and `TypeScriptNaming.ToCamelCaseMember`. These strings live in
  `Metano.Compiler.TypeScript`; core holds none (NFR-002).
- **Dart** supplies `null` for 3.0 (DECIDED-35, open question OQ-9), so its output does not change.
- The groups' cache is keyed by the policy (a record with value equality) instead of the target.

**The group key and identity.** A group keeps its C# name as its identity (`GroupNameOf`), so
`IrOverloadRef(typeKey, groupName, ordinal)` of every declared member is unchanged. Joining is
decided by **spelling**: a synthesized member joins the declared group whose spelling
(`[Name]` override when present, else `MethodSpelling(GroupNameOf(m))`) equals its emitted name.
So `GetHashCode` joins the group `HashCode` (spelled `hashCode`), and `Equals(RTol, double)`'s
group `Equals` (spelled `equals`). The synthesized member's ref is
`IrOverloadRef(typeKey, <that group's name>, <its ordinal, after every declared member>)`.
Declared ordinals do not move, so a call site and a cached group compute the same refs.

## 3. When a member is inserted (FR-040)

For each kind the type synthesizes (`IrValueEqualitySynthesis` and `IrRecordEqualityOverrides`
decide this, read through a symbol-level adapter that calls the same predicates, FR-045):

1. **A user group shares its spelling** (only instance methods, not operators): the synthesized
   member joins that group. A lone user method makes a group of two, which is dispatched under
   the shared name.
2. **An emitted base joined the same kind** (§ 4): the member is inserted, anchored on the base's,
   even when no user group of the type shares the name.
3. Otherwise nothing is inserted, and the target emits the member as today (NFR-001: no churn).

What the type itself declares replaces a kind and blocks the insertion, as C# does: a record's
`Equals(R)`, a struct's `Equals(object)` override or `Equals(S)`, and a `GetHashCode()` override.
`Copy` cannot be replaced.

Per kind:

- **Record `Equals(object)`.** It joins an `Equals` overload such as `Equals(RTol other, double
  tol)` or `Equals(ROverl o, int slack)` / `Equals(ROverl o, string tag)` (three overloads, one
  dispatcher). `valueEquals(a, b)` calls `equals(b)`, which takes the one-argument branch: the
  synthesized one, unless a user one-argument overload whose parameter has a runtime check
  matches first.
- **Struct `Equals(object)`.** The member is the data-described `ValueType.Equals`; there is no
  symbol to read and none is needed.
- **`GetHashCode()`.** It joins a user `HashCode(int seed)` through the spelling. `HashCode.add`
  calls `hashCode()`, which takes the zero-argument branch.
- **Record copy (`with`).** It joins a user `With(int a)`. A `with` expression binds statically to
  the synthesized fast path (§ 5), so the dispatcher only serves calls through an interface, a base
  type or hand-written code. Its branch takes an optional `object`, has no runtime check, and is
  ordered after every overload that has one.

**"Cannot tell apart" (FR-042, MS0001 error).** A user overload in a joined group is reported, as
an error at the user method, when its argument-count range overlaps the synthesized member's and
none of its overlapping parameters has a runtime check (`IrTypeCheckBuilder.HasCheck`, the rule
`CatchAllOverloads` already uses). Then no dispatcher order can route both members:

- `With()` or `With(object)` against the synthesized `with(overrides?)`;
- `GRec<T>.With(T v)`, since a type parameter has no check;
- `HashCode()`, which has the same arity as `hashCode()`;
- `Equals(object o)` declared as an overload rather than an override.

The message names both members and suggests a rename or a parameter of a checkable type. The
round-4 patch's `ValueEqualityJoinReporter` carries this rule.

## 4. Anchoring across a hierarchy (FR-041)

- **Stable fast-path name.** A synthesized member's suffix is pinned to the contract's
  `FastPathSuffix` (an `OverloadSuffixPin` built from data), so its fast path is
  `<emitted name><suffix>`: `equalsByValue`, `hashCodeByValue`, `withByValue`. It is the same at
  every level of a hierarchy and in every package. The claim pass claims it as a method fast path
  (naming.md step 5) and renames it with `$` and `MS0067` only when a user member already holds it.
- **The anchor.** A `SynthesizedMember` of kind `K` in type `D` is anchored on `K`'s member in the
  nearest emitted base `B` that inserted one (rule 2 of § 3). It takes the base's fast-path name and
  is emitted with `override`. A base whose `K` was not inserted (it kept its plain `equals`) is no
  anchor: `D` then inserts `K` only through rule 1.
- **The worked hierarchy** (the round-4 failures, compiler-man R3 probes `ABase` / `ADer`):

  ```csharp
  abstract record ABase(double V) { public virtual bool Equals(ABase other, double tol) => false;
                                    public abstract int HashCode(int seed); }
  record ADer(double V) : ABase(V) { public override bool Equals(ABase o, double t) => …;
                                     public override int HashCode(int s) => s + 1; }
  record AOther(double V) : ABase(V) { public override int HashCode(int s) => s; }
  ```

  - `ABase`: group `Equals` = {`Equals(ABase, double)` → `equalsOtherTol`, synthesized → `equalsByValue`}, with dispatcher `equals`. Group `HashCode` = {abstract `HashCode(int)` → `abstract hashCodeSeed`, synthesized → `hashCodeByValue`}, with dispatcher `hashCode`.
  - `ADer`: the override anchors on `ABase`'s root, so it is `override equalsOtherTol(o, t)` (the root's suffix, whatever `ADer` names its parameters; FR-025). The synthesized member anchors on `ABase`'s, so it is `override equalsByValue(other)`. The group adds no overloads, so there is no dispatcher (FR-030). `ABase`'s dispatcher reaches both virtually. The same holds for `HashCode`.
  - `AOther` declares no `Equals(…, tol)`. Rule 2 inserts its synthesized `Equals`, anchored: `override equalsByValue(other)` and no dispatcher. A two-argument call through `ABase` therefore still reaches `equalsOtherTol` (round 4 lost it).
- **`super` without recursion.** A derived synthesized body chains to its base's members through
  the **base fast path**, never through the base dispatcher: `return super.equalsByValue(other)
  && …` and `hc.add(super.hashCodeByValue())`. In round 4 the derived body called
  `super.equals(other)`, whose dispatcher called `this.equalsByValue` (the derived one) again. When
  the base did not join, the body calls `super.equals(other)` as today.
- **Abstract and virtual user overloads** follow FR-030 unchanged: an abstract one is
  `abstract <fastPath>(…)` beside the synthesized fast path, and a virtual one is anchored by its
  overrides.
- **A derived type that joins when its base did not** (`D : B`, where `D` declares
  `Equals(D, double)` and `B` kept its plain `equals`): `D`'s group is {user, synthesized}, with a
  dispatcher `override equals(...)`. Its synthesized body calls `super.equals(other)` (`B`'s plain
  member). Calls through `B` reach `D`'s dispatcher.

## 5. Direct call sites (FR-043)

- A call that binds to a declared overload of a joined group is a direct fast-path call (FR-018),
  as for any dispatched group (`a.equalsOtherTol(b, 0.1)`, `a.withA(7)`, `a.hashCodeSeed(2)`).
- A C# call that binds to the synthesized member resolves to its ref and calls its fast path
  directly: the implicit `Equals(object)` / `Equals(R)`, a struct's `ValueType.Equals`, and
  `GetHashCode()` on a joined type (`a.equalsByValue(b)`, `a.hashCodeByValue()`). A `with`
  expression on a joined record calls `a.withByValue({ … })`. On a type that did not join, these
  stay as today (`a.equals(b)`, `valueHashCode(a)`, `a.with({…})`).
- Calls through an interface, `object`, a type parameter or the runtime keep the shared name and
  are dispatched by argument count and runtime checks (OverloadDispatchOrder): `valueEquals`,
  `HashSet`, `IEqualityComparer<T>.Equals(a, b)`, `ITolEq.Equals(other, tol)`.

## 6. Interfaces (FR-044)

A user method that implements an interface member keeps the shared name through the class
dispatcher:

- `SCmp : IEqualityComparer<SPt>` (`Equals(SPt, SPt)`, `GetHashCode(SPt)`) and `RCmp : IEqualityComparer<int>`. The BCL interface is not emitted, so each implementation is its own root. `Equals(SPt, SPt)` joins with the synthesized `Equals` (arity 2 against arity 1). `GetHashCode(SPt)` is spelled `getHashCode` and joins nothing.
- `ITolEq.Equals(ITolEq other, double tol)` on `ITolRec`: the interface root is a single member, not dispatched, so the implementation is not anchored. It joins with the synthesized member, and the class dispatcher's overload signatures include `equals(other: ITolEq, tol: number)`, which satisfies the interface.
- `IWither.With(int)` on `WRec` and `IHashSeed.HashCode(int)` on `SHashSeed`: the same, through `with` and `hashCode`.
- An interface whose root group **is** dispatched anchors the implementation on its names (FR-025). The synthesized member joins the class group beside it.

## 7. Fields and properties under a synthesized name (FR-042)

Data members are not grouped. The target's member table decides, as today:

- A field or property emitted as `equals` reports `MS0001` as an **error** at the member, naming its
  kind (field or property). It cannot join a dispatcher nor be renamed.
- A data member emitted as `with` cannot occur in TypeScript (`with` is reserved and escaped to
  `with_`). If another target spells one, it reports like `equals`.
- A field `HashCode` keeps `hashCode`, and the synthesized `hashCode` is skipped (KEntry, RHash).
  The record still compares through `equals`; the runtime reads a hash through `hashCode()` only
  when it is a function.

## 8. Cross-package consistency (FR-039, DECIDED-28)

The decision is a pure function of the symbols and the policy, and the policy is fixed per target.
A consumer runs it over the producer's metadata symbols (DECIDED-28) and computes the same
insertions, anchors and fast-path names. That covers:

- a consumer record deriving from a library record that joined (`override equalsByValue`);
- a consumer calling a library record's joined group (`lib.equalsOtherTol(…)`);
- metadata-only references, which keep the dispatcher (FR-019).

A reference assembly hides nothing that matters here: the decision reads only public declared
methods and the record or struct shape.

## 9. Dart

Dart supplies no names for 3.0 (DECIDED-35), so no member is inserted and its output is
byte-identical. That is checked on the Dart goldens and the Flutter sample's generated files.
Dart's `hashCode` getter would collide with a user `HashCode(int)` method as a getter, not a
method, so joining needs a Dart design of its own (OQ-9).

## 10. Test plan

**Probe rows** (.NET 10 against Bun and Node, each process with a timeout), from compiler-man's R3
set plus the earlier rounds:

- **Joins:** `ROverl` (`TTFFT`); `RTol` / `QOuter` (`TTFT`); `QWith` (`5b7bT`); `QHashM` (`3TTT`); `KTol`; `StructClashProbes.*`.
- **Fields:** `KEntry` and `RHash` (`7kT`, `T1`).
- **Interfaces:** `SCmp` direct (`T4T`) and through `IEqualityComparer` (`T4`); `RCmp` (`T2T`, the `Dictionary` row); `ITolEq` (`TTTF`, `T`); `IWither` (`5|3|9`); `IHashSeed` (`3|4TT`).
- **Other shapes:** `named` (`[Name]` on a joined overload); `staticEq` (a static `Equals` never joins); `methodGroup`; `nullCond`; `generic`.
- **Hierarchy:** `ABase` / `ADer` / `AOther` (`TT34TT`, no hang), plus a derived type joining over a non-joined base.
- **Errors:** `GRec<T>.With(T)` (MS0001 error); `QEqualsProp` (MS0001 error naming the property).
- **Cross-package:** the R3 `xpkg` consumer (a library record with a joined `Equals`, a consumer subclass and calls).

**Tests (TUnit):**

- `ValueEqualityJoinTests`: one test per row above, asserting the emitted dispatcher, fast paths, `override` flags, call sites and diagnostics.
- A cross-package test using `TranspileWithLibrary`.
- A Dart test that the Dart goldens do not change.

**Goldens:** a new `value-equality-join.ts` (records, a struct, a hierarchy, compiled with `tsgo --strict`). Existing goldens change only where a group joins; none of today's goldens have one.

**Gates:** all of brief-common.md; tsgo errors on the probe project must not grow.

## 11. Expected size

| Area | Estimate |
|---|---|
| Core (`MethodOverloadGroups`, `OverloadGroupNaming`, `IrClassExtractor`, `DispatchedMethodCalls`, the policy record, the symbol adapter for the IR predicates, the IR marker) | ≈ 450–600 lines changed |
| TypeScript (contract supply, synthesized bodies under claimed fast-path names, `super` fast-path chaining, `with` / `equals` / `hashCode` call-site rendering, reporter) | ≈ 300 lines |
| Tests | ≈ 500 lines |
| Spec and docs | this contract plus FR edits |
| Removed | the round-3 rename (`ValueEqualityMemberNames`, `YieldsToValueEquality`, ≈ 90 lines) and its tests |
| Work | about two to three implementation rounds, including probes |
