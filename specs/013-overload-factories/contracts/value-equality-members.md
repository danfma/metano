# Contract: Synthesized value-equality members in overload groups

**Normative for**: 013 FR-038–FR-045, DECIDED-33–DECIDED-35; baseline FR-060.
**Status**: implemented in wave 3.0 round 6, after review (architecture: approved with
reservations; compiler-man: approved with changes). "As implemented" notes record where the code
settles a detail the review left open. It replaces the round-3 forced rename
(`ValueEqualityMemberNames`, `MethodOverloadGroup.YieldsToValueEquality`) and the round-4
TypeScript-only join, which could not anchor overrides or inherit dispatchers.

## Problem

A record or a plain struct emitted as a class has value-equality members that C# synthesizes or
inherits (baseline FR-060): `Equals(object)`, `GetHashCode()` and, for a record, the copy behind
a `with` expression. A target emits them under the names its runtime calls; TypeScript's
`valueEquals`, `HashSet` and `Dictionary` call `equals(other)` and `hashCode()`.

A user method can share that spelling: `Equals(RTol other, double tol)` → `equals`,
`HashCode(int seed)` → `hashCode`, a record's `With(int a)` → `with`, or an implementation of
`IEqualityComparer<T>.Equals(T, T)`. Today one of the two members is lost.

The synthesized member therefore **joins** the user group as one more overload, and spec 013's
machinery does the rest: names, root anchoring, inherited dispatchers forwarding to `super`,
`override` flags, direct call sites and the claim pass.

## 0. The neutral fact: `ValueEqualityShape` (FR-045)

Core computes one target-neutral fact per class from its symbols and stores it on the IR:

```csharp
public enum IrValueEqualityKind { None, Record, Struct }

public sealed record IrValueEqualityShape(
    IrValueEqualityKind Kind,       // None for [PlainObject], [JsTuple], [Branded], JSX
                                    // components and every non-record class
    bool ReplacesEquals,            // record: a declared Equals(R); struct: a declared
                                    // override Equals(object), or an Equals(S) that
                                    // implements an interface member
    bool ReplacesHashCode);         // a declared override GetHashCode()
```

- The fact is stored as `IrTypeSemantics.ValueEquality`.
- `IrValueEqualitySynthesis` and `IrRecordEqualityOverrides` become readers of that field. So do their callers: the TypeScript record synthesis, the Dart class bridge and `IrRuntimeRequirementScanner`.
- The name-and-arity heuristic over IR members (`IsRecordTypeRef`, `DeclaresStructEquals`) is retired.
- A synthesized `IrMethodDeclaration` (§ 1) can never count as a user `Equals` or `GetHashCode`: the shape is read from symbols, before any synthesized member exists.

**A struct's `Equals(S)` (round 7).** It replaces the synthesized equality when it implements an
interface member: `IEquatable<S>.Equals`, implicitly or explicitly, or a transpiled interface's
`Equals(S)`.

- **Why.** .NET's `EqualityComparer<S>.Default` calls `IEquatable<S>.Equals` for sets, dictionaries, `Distinct` and record fields, and a caller through the interface calls it too.
- **Alone,** it stays the type's `equals`, as on main.
- **Beside another `Equals` overload,** the synthesized member routes to it, as it does to a record's `Equals(R)`.
- **The cost:** a boxed comparison (`object.Equals`, a `ValueType.Equals` call) also reaches the user's `Equals(S)`, where .NET compares fields. This is main's behavior for that shape.
- **A struct's `Equals(S)` that implements no interface member replaces nothing,** since `ValueType.Equals` never calls it. It is shadowed and reports `MS0063` (§ 3).
- An explicit `IEquatable<S>.Equals` on a struct is emitted under the interface member's name. `System.ValueType`'s inherited `Equals` claims no name, as `object`'s does not.

## 1. The group member (FR-038)

A group member becomes a small closed hierarchy. The facts that naming and dispatch need live on
the base type, so only `IrClassExtractor` pattern-matches on the case:

```csharp
internal abstract record GroupMember(
    int Ordinal,                        // declaration order; a synthesized member comes after
                                        // every declared one
    MemberAnchor? Anchor)
{
    public abstract bool IsStatic { get; }
    public abstract int RequiredArity { get; }
    public abstract int MaximumArity { get; }
    public abstract bool StandsFor(IMethodSymbol method);   // a declared symbol, or the
                                                            // replacement folded in (§ 3)
}

internal sealed record DeclaredMember(IMethodSymbol Symbol, int Ordinal, MemberAnchor? Anchor)
    : GroupMember(Ordinal, Anchor);
internal sealed record SynthesizedMember(
    IrSynthesizedValueMember Kind, int Ordinal, MemberAnchor? Anchor,
    IMethodSymbol? Replacement = null) : GroupMember(Ordinal, Anchor);

// One anchor for both cases.
internal abstract record AnchorRoot;
internal sealed record DeclaredRoot(IMethodSymbol Method) : AnchorRoot;
internal sealed record SynthesizedRoot(INamedTypeSymbol Owner, IrSynthesizedValueMember Kind)
    : AnchorRoot;
internal sealed record MemberAnchor(
    AnchorRoot Root,
    IrOverloadAnchorKind Kind,
    IReadOnlyList<IMethodSymbol> FurtherRoots,
    IReadOnlyList<IMethodSymbol>? ReimplementedRoots = null);
```

**As implemented.**

- **The anchor is not a bare `IrOverloadRef`.** The root suffix of a declared root is read from its symbol (its group and `[Name]` pin), so the anchor names its root (`AnchorRoot`). The class extractor turns it into the ref (`IrOverloadAnchor.Root`) with `RefOf` / `SynthesizedRefOf`. Further roots are always interface members and stay symbols.
- **The suffix pin is not stored on the member.** Naming reads a declared member's `[Name]` (or its root's), and a synthesized member always takes the policy's suffix.

- `IrSynthesizedValueMember` (`Equals`, `HashCode`, `Copy`) is the single enum for the kind, used by core and the IR alike.
- `SynthesizedMember` needs no `Owner`: a group belongs to one type.
- In the IR, a synthesized member is an `IrMethodDeclaration` with `Semantics.IsSynthesized` and `SynthesizedKind`. Its `Body` takes one of three forms:
  - the user's body folded in (§ 3);
  - for a record that declares `Equals(R)`, the route C# takes, as ordinary IR: `return this.Equals(other is R ? (R)other : null);`, its call bound to the user overload's ref;
  - null otherwise: the target supplies the body it already synthesizes.
- A declared overload the dispatcher cannot tell from the synthesized member carries `IsShadowedBySynthesized` (§ 3).
- Its shape is fixed by its kind:

  | Kind | Parameters (dispatch shape) | Return |
  |---|---|---|
  | `Equals` | `other: object` | `bool` |
  | `HashCode` | none | `int` |
  | `Copy` | `overrides: object`, optional | the record |

### Sites that change

| File | Change |
|---|---|
| `MethodOverloadGroups.cs` | `GroupMember`; `GroupsNamed` inserts the synthesized member (§ 3); `SuffixesOf` is keyed by `GroupMember`; anchors are ref-based; `RefOf(GroupMember)`; new `SynthesizedOf(type, kind, policy)`; every entry point takes the policy |
| `OverloadGroupNaming.cs` | `PinnedOverload(GroupMember, OverloadSuffixPin?)`; a synthesized member is always pinned |
| `IrClassExtractor.cs` | keyed by `GroupMember`; builds the synthesized member's IR; folds a replacing `Equals(object)` / `GetHashCode()` into it (§ 3); the only place that matches on the case |
| `DispatchedMethodCalls.cs` | declared bindings unchanged; adds the bindings of § 5 |
| `IrExpressionExtractor.cs` | `base.Equals(x)` / `base.GetHashCode()` in a joined type resolve to the base's synthesized ref; passes the policy |
| `OverloadEmittedName.cs` | `NameOverrideOf(method, policy)` and `PinsOverloadSuffixes(type, policy)` take the policy (A4); **this file changes** |
| `IrParameterNaming.cs`, `IrPropertyExtractor.cs`, `OverrideChains.cs`, `OverridablePropertyConstructionReads.cs`, `PrimaryConstructorPropertyAlias.cs`, `MultiplyConstructedInterfaces.cs` | pass the policy where they call the grouping family (A4) |
| Targets | `IrToTsNamingPolicy` (TypeScript) and `DartTypeShapes` (Dart) pass their target's single static policy instance |

## 2. The target contract (FR-039)

```csharp
public sealed record SynthesizedMemberNames(
    string? EqualsName, string? HashCodeName, string? CopyName, string FastPathSuffix);

public sealed record OverloadGroupingPolicy(
    TargetLanguage? Target, SynthesizedMemberNames? ValueEqualityNames);
```

- **Value equality.** Both records hold only strings, so they compare by value and can key the groups' cache (A1).
- **One instance per target.** TypeScript holds `new(TargetLanguage.TypeScript, new("equals", "hashCode", "with", "ByValue"))`. Dart holds `new(TargetLanguage.Dart, null)` for 3.0 (DECIDED-35, OQ-9 decided), so no Dart member joins and Dart output is unchanged. The TypeScript strings live in `Metano.Compiler.TypeScript`; core holds none.
- **One source of `Target`.** `IrExtractionContext` takes the policy in its primary constructor and projects `Target` from it.
- **The policy replaces `TargetLanguage?` across the grouping family** (A4): every `MethodOverloadGroups` entry point, `OverloadEmittedName.NameOverrideOf` / `PinsOverloadSuffixes`, and their callers listed in § 1.
- **Spelling (A2, A3).** A dispatched group is spelled `SymbolHelper.ToCamelCase(GroupNameOf(member))`, since a `[Name]` on its overloads pins suffixes. A lone method is spelled by its raw `SymbolHelper.GetNameOverride(member, target)` when it has one, else by its camelCased name. The raw override is used, never `OverloadEmittedName.NameOverrideOf`, which would re-enter `GroupOf(...).IsDispatched`.
  - `[Name("near")] Equals(NRec, double)` is spelled `near` and joins nothing.
  - `[Name("equals")] Same(NEq, double)`, `[Name("hashCode")] Seeded(int)` and `[Name("with")] Replace(int)` join the `equals` / `hashCode` / `with` members.
  - The method whose `[Name]` named the group (`MethodOverloadGroup.NamingMember`) pins no suffix: its fast path is named over its parameters (`equalsOTol`).
  - The target names a group holding a synthesized member after that member's kind, never after the group's C# name.
  - `GetHashCode` joins the group `HashCode`.
- **Identity.** A group keeps its C# name as its identity (`GroupNameOf`), so every declared member's `IrOverloadRef` is unchanged. A synthesized member's ref is `(typeKey, joined group's name, ordinal after every declared member)`.
- **Explicit interface implementations** (C5): `bool IEquatable<int>.Equals(int o)` and `IWither IWither.With(int a)` group under their interface member's name (`GroupNameOf`), so their spelling is `equals` / `with`. They join when the implementation is emitted under the interface name; dropped ones never do.

## 3. When a member is inserted, and its body (FR-040)

For each kind `K` the shape synthesizes, whose target name is non-null:

1. **A user instance method group shares `K`'s spelling** (operators and statics never): `K` joins it. A lone user method gives a group of two, dispatched under the shared name. A group whose only members are the declarations that replace `K` (a record's lone `Equals(R)`, a struct's lone `override Equals(object)`) does not count: those declarations are C#'s implementation of `K` itself, and they are emitted under its name as today (NFR-001).
2. **The nearest emitted base inserted `K`** (§ 4): `K` is inserted, anchored, even when nothing in the type shares the name.
3. Otherwise nothing is inserted, and the target emits `K` as today (NFR-001).

**A user declaration that replaces `K` does not remove `K`** (C1): C# still has `Equals(object)` and `GetHashCode()` on the type; the user declaration supplies their behavior.

- **Replacing by the same signature.** `override Equals(object)` on a struct, or `override GetHashCode()`: the user body is folded into the synthesized member, and the user method is not emitted on its own (one `hashCode`, not two).
  - UHash: `override GetHashCode() => 7` beside `HashCode(int seed)` prints `7|3T`.
  - UObjEq: `override Equals(object)` beside `Equals(UObjEq, int)`.
- **Replacing by a typed `Equals(R)` on a record.** The synthesized `Equals(object)` routes to it as C# does: `this.<userFastPath>(other instanceof R ? other : null)`.
  - UBoth: a user `virtual Equals(UBoth?)` beside `Equals(UBoth, double)`; behind `object`, .NET prints `False`, where today the dispatcher throws "No matching overload".
  - A derived record with its own `Equals(D)` under a joined base answers through its own equality: its synthesized member overrides the base's and routes to `Equals(D)`.
- **A struct's interface-implementing `Equals(S)` is routed to like a record's `Equals(R)`** (§ 0): `return other is S ? this.Equals((S)other) : false`. SEqJoin: `Equals(SEqJoin)` (`IEquatable`) beside `Equals(SEqJoin, int)`.
- **A struct's `Equals(S)` that implements nothing replaces nothing** (§ 0). USBoth: `Equals(USBoth)` beside `Equals(USBoth, int)`. The synthesized member compares fields, and the C# call `a.Equals(b)` binds to the user's `Equals(S)` (§ 5).

### Per kind

- **`Equals`** (record or struct, described by data). The dispatcher tries the synthesized branch **first**, guarded by "the argument has the same runtime type as `this`" (C2). The user's one-argument branches follow, and an unguarded synthesized branch closes the one-argument range, so a foreign argument returns `false` instead of throwing.
  - `valueEquals`, `HashSet` and `Dictionary` reach the synthesized member for same-type values.
  - UOpt: `Equals(UOpt o, double tol = 0.5)`; .NET prints `FFTFTF`.
  - UIface: `Equals(IUShape)` on a record implementing `IUShape`; .NET prints `TFFFF`.
- **`HashCode`** joins a user `HashCode(int seed)` through the spelling. `HashCode.add` calls `hashCode()` and takes the zero-argument branch. HashCode cases:
  - **A user `HashCode()` with no parameters** has the exact shape of the synthesized member, so it cannot be told apart (rule C3 below).
  - **`HashCode(int seed = 0)`** overlaps the zero-argument range, so it is not distinguishable at zero arguments: it keeps its fast path and is reported with MS0063. Zero-argument calls through the dispatcher reach the synthesized member; C# calls bind statically.
- **`Copy`.** A `with` expression binds statically to the synthesized fast path (§ 5), so the dispatcher's copy branch serves only interface, base and hand-written calls. Its guard is "a plain object or `undefined`".
  - `GRec<T>.With(T v)` keeps building and prints `TT74`.

### Indistinguishable user overloads (C2, C3; FR-042)

A user overload is **distinguishable** from the synthesized member at an argument count they both
accept only when each of its parameters there has a runtime check **and** that check cannot
accept the owner type (`Equals(IUShape)` on `UIface : IUShape` accepts the owner).

**As implemented**, core decides it from symbols at the fewest arguments both accept:

- **No runtime check:** a type parameter, `object`, `dynamic` or `ValueType`.
- **The check accepts the synthesized argument:**
  - for `Equals`, the owner type, a base or an interface of it (a `Nullable<T>` unwrapped);
  - for the copy, any interface;
  - at zero arguments there is no parameter to check, so any overlap there counts.

The dispatcher order is:

1. the synthesized branch guarded by the same runtime type;
2. the distinguishable branches;
3. the synthesized closing branch;
4. the shadowed overloads, at the argument counts the synthesized member does not accept.

A shadowed overload whose argument counts the synthesized member covers entirely has no branch.

- An indistinguishable overload keeps its fast path, so its C# call sites stay direct, but it gets **no dispatcher branch**. It is reported with the existing `MS0063` warning, except when the type replaces `Equals` itself (a record's `Equals(R)`, a struct's `override Equals(object)` or interface-implementing `Equals(S)`). Calls through the dispatcher then reach the user's own equality. That check runs first, before the interface check below.
- The synthesized member keeps the shared name and its branch.
- The case is an **error** (`MS0001`) only when that overload implements a member of a transpiled interface: callers through the interface would reach the synthesized member instead.
  - Example: `interface IHasHash { int HashCode(); }` on a record.
  - **An override needs no such check.** It is anchored on its base's group, which the synthesized member joins too (rule 2), so it has no branch of its own and its C# calls bind statically. The shadowed root reports once, at the base.
- Examples:
  - `GRec<T>.With(T v)` and `Equals(IUShape)`: MS0063, and the code keeps building.
  - A zero-parameter `HashCode()`: MS0063.
- **The fast-path name is invariant** (C4). On a name clash the **user** member is renamed (`$`, MS0067), never `equalsByValue`, `hashCodeByValue` or `withByValue`.
- **Only targets that supply names are affected.** These rules apply only when the target supplies the kind's name; Dart supplies none.

## 4. Anchoring across a hierarchy (FR-041)

- **Stable names.** The fast path is `<name><FastPathSuffix>`: `equalsByValue`, `hashCodeByValue`, `withByValue`, the same in every level and package.
- **The anchor.** A synthesized `K` in `D` anchors (`MemberAnchor.Root` = that member's ref) on `K` in the nearest emitted base that inserted it. It is emitted with `override`, and with no dispatcher when its group adds no overloads (FR-030). A base that did not insert `K` is no anchor.
- **The round-4 hierarchy** (compiler-man R3 `ABase` / `ADer`, plus `AOther`):
  - `ABase` has the dispatcher `equals` over `equalsOtherTol` and `equalsByValue`, and the dispatcher `hashCode` over `abstract hashCodeSeed` and `hashCodeByValue`.
  - `ADer` emits `override equalsOtherTol(o, t)` (the root's suffix, FR-025), `override equalsByValue`, `override hashCodeSeed` and `override hashCodeByValue`, with no dispatchers.
  - `AOther` declares no `Equals(…, tol)`: rule 2 inserts its synthesized members, anchored.
- **Three levels where only the middle type joins.** The top keeps its plain `equals`. The middle declares a dispatcher `override equals(...)` over its synthesized member and its user overload; its synthesized body calls `super.equals(other)`. The bottom anchors on the middle.
- **`super` without recursion.** A derived synthesized body chains to the base **fast path** (`super.equalsByValue(other)`, `super.hashCodeByValue()`), never through the base dispatcher; that recursion is what hung in round 4. When the base did not join, it calls `super.equals(other)` as today.
- **tsgo.** A derived `override equals(...)` with overload signatures over a base `equals(other: any)` was confirmed against tsgo in round 4 and is checked again in the golden.

## 5. Direct call sites (FR-043; OQ-10 decided: bind)

- A call bound to a declared member of a joined group is a direct fast-path call (FR-018).
- A call bound to a synthesized member on a joined type calls its fast path. Binding to the synthesized member is needed for correctness: through the dispatcher, a same-type argument and a user typed overload would compete. The bindings are:
  - the implicit `Equals(object)` / `Equals(R)` of a record (`Equals(Base)` too, on a derived record), and a struct's `ValueType.Equals` → `a.equalsByValue(b)`;
  - `GetHashCode()` → `a.hashCodeByValue()`;
  - a `with` expression → `a.withByValue({ … })`.
- On a type that did not join, these stay as today.
- Calls through an interface, `object`, a type parameter or the runtime keep the shared name and are dispatched.
- **Limitation (documented).** Runtime dispatch differs from C# for a foreign argument that a user overload accepts: `record UNum(double V) { bool Equals(double v) }` with `object.Equals(a, 1.0)` is `false` on .NET (the boxed `Equals(object)`), but the dispatcher takes the user `Equals(double)` branch.

## 6. Interfaces (FR-044)

- `IEqualityComparer<SPt>` on `SCmp`, and `IEqualityComparer<int>` on `RCmp`:
  - `Equals(T, T)` joins at two arguments, against the synthesized one-argument member, and is distinguishable.
  - `GetHashCode(T)` is spelled `getHashCode` and joins nothing.
- A transpiled `ITolEq.Equals(ITolEq, double)`, `IWither.With(int)` and `IHashSeed.HashCode(int)` join, and the class dispatcher's overload signatures satisfy the interface.
- An implementation that is indistinguishable **and** implements an interface member is an error (§ 3).
- An interface whose root group is dispatched anchors the implementation on its names (FR-025).

## 7. Fields and properties under a synthesized name (FR-042)

Data members are not grouped; the target's member table decides:

- A field or property emitted as `equals` reports `MS0001` as an error naming its kind (field or property). The `with` case cannot occur in TypeScript, which escapes the name to `with_`.
- A field `HashCode` keeps `hashCode`, and the synthesized `hashCode` is skipped (KEntry, RHash).

## 8. Cross-package consistency (FR-039, DECIDED-28)

Insertion, anchoring and naming are pure functions of the symbols and the per-target policy, so a
consumer computes the same result over the producer's metadata:

- a consumer record deriving from a library record that joined (`override equalsByValue`);
- direct calls into a joined group;
- metadata-only references keep the dispatcher (FR-019), except calls bound to a synthesized member (`a.Equals(b)`, `base.GetHashCode()`, `with`). Their fast path keeps its stable name in every package (`equalsByValue`, DECIDED-34), so the call carries the member's kind (`IrSelectedOverload.Synthesized`) and the target names it even when the producer's claims are unreadable. A `base.` call through the base dispatcher would recurse into the derived override.

**Compiler-generated members in metadata.** A referenced record's synthesized `Equals(R)`,
`Equals(object)` and `GetHashCode()` are not implicitly declared when read from metadata; they
carry `[CompilerGenerated]`, which survives in reference assemblies. The groups and the shape
exclude them as they exclude implicit members, so a consumer computes the producer's groups.

**Non-public colliding overloads across a reference assembly** (C5). A `private Equals(R, double)`
joins in the producer, which emits a dispatcher `equals` and `equalsByValue`. A reference assembly
strips it, so the consumer sees no colliding group and inserts nothing. That is safe:

- the consumer's own calls bind to the record's public members; synthesized ones call `equals(…)` / `with(…)` dynamically, which the producer's dispatcher serves;
- a consumer subclass inserts `K` only through rule 2. It reads "the base inserted `K`" from the base's **public** groups, so it may decide "not inserted" and emit its synthesized `equals` as a plain override of the producer's dispatcher name. That keeps working for one-argument calls; the producer's private two-argument overload is not callable from the consumer anyway.

A test covers it.

## 9. Dart

The Dart policy supplies no names (DECIDED-35), so nothing is inserted and the Dart goldens and
the Flutter sample stay byte-identical.

## 10. Test plan

**Probe rows** (.NET 10 against Bun and Node, every process under a timeout):

- compiler-man R3: ROverl, SCmp direct and through `IEqualityComparer`, RCmp, ITolEq, IWither, IHashSeed, named, staticEq, methodGroup, nullCond, generic, `GRec<T>.With(T)` (builds, `TT74`).
- Design probes: UBoth, USBoth, UOpt (`FFTFTF`), UIface (`TFFFF`), UNum (the documented limitation), UHash (`7|3T`), UObjEq.
- Hierarchies: ABase / ADer / AOther with a timeout; three levels where only the middle joins; a derived type with its own `Equals(D)` under a joined base.
- Earlier rows: RTol / QOuter, QWith, the `HashCode(int)` struct, KEntry / RHash, KTol, `StructClashProbes`, and the R3 `xpkg` consumer.
- No row worse than base or `7d7d7e2f`; tsgo errors on the probe project do not grow.

**Tests (TUnit):**

- `ValueEqualityJoinTests`, with one test per row family: dispatcher, fast paths, `override`, call sites, diagnostics (MS0063, MS0001, MS0067).
- A cross-package test with a public and a private colliding overload.
- A Dart test that output is unchanged.
- The shape fact.

**Golden:** `value-equality-join.ts`, compiled with tsgo `--strict --noImplicitOverride`.

**Limitation (documented, § 5):** UNum prints `TTFT` where .NET prints `TFFT`. A user `Equals(double)` is distinguishable, so `object.Equals(a, 1.0)` takes its branch.

## 11. Expected size

| Area | Estimate |
|---|---|
| Core | ≈ 700–900 lines: the shape fact and its readers; the policy threaded through the grouping family; the member hierarchy and the ref-based anchor; insertion, folding, call sites |
| TypeScript | ≈ 350 lines: the policy instance, synthesized bodies under claimed fast paths, the guarded `Equals` branches, `super` chaining, call-site rendering |
| Tests | ≈ 600 lines |
| Removed | the round-3 rename and the IR name-and-arity heuristic |
