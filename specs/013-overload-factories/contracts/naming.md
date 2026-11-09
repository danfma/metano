# Contract: The overload naming algorithm

**Normative for**: 013 FR-001–FR-006, FR-024–FR-026, FR-030, FR-032–FR-034, FR-036. One algorithm names the
constructor factory, the selector symbol, the method fast path and the operator fast path of every
overload of a dispatched group. It has two stages:

1. **Candidates (core, `OverloadNamePolicy`, DECIDED-1).** A pure function of the overload group's
   C# declarations, read from the naming spellings captured from Roslyn at extraction time (M1,
   FR-002) — never from the IR type refs, which erase collections, delegates, unsigned primitives
   and containing types (R7).
2. **Claims (each target bridge, DECIDED-18).** The target resolves the candidates against its own
   member tables in one fixed order (§ Claim pass) and reports `MS0067` for every rename.

Evidence for every "today" row: research R7.

## Stage 1 — candidates

For each overload `o` of a group `G` (two or more overloads of one constructor, method, operator
or module function; for virtual and interface members, the **root** group of § Root anchoring):

1. **Suffix, pass 1 — parameter names.** `string.Concat(o.Parameters.Select(p => Capitalize(p.Name)))`:
   `Add(int width, int height)` → `WidthHeight`; `Format(string fmt, params object[] args)` →
   `FmtArgs`. **No joiner** (DECIDED-2). A parameterless overload's suffix is `Default`
   (DECIDED-5). Defaults, nullability and `params` do not change the suffix. An extension method's
   receiver parameter (`this Base b`) is a parameter like any other in every pass, except when all
   overloads of the group declare it with the same name and type: then it is left out of every
   pass (a nullable annotation is part of the type: `this string? s` and `this string s` are
   different receivers) (`Twice(this Base b, int x)` / `Twice(this Base b, string s)` → `twiceX` /
   `twiceS`, R11;
   `Twice(this Base b, int x)` / `Twice(this Other o, int x)` → `twiceBX` / `twiceOX`).
2. **Distinctness, pass 1.** If every suffix in `G` is distinct (ordinal), done.
3. **Suffix, pass 2 — type spellings.** On any collision, **every** overload of `G` switches to the
   § Spellings of its parameter types, concatenated in order (`F(int a)` / `F(string a)` →
   `Int32` / `String`). Operator groups start here: their suffix is always the type spellings
   (FR-033, today's convention). A conversion operator group (`op_Implicit` / `op_Explicit`)
   names like a method, but its type spelling ends with `To` + the target type's spelling,
   because C# tells conversions apart by their target type (`explicit operator int(Money m)` /
   `explicit operator long(Money m)` → `MoneyToInt32` / `MoneyToInt64`).
4. **Distinctness, pass 2.** If every suffix in `G` is distinct, done.
5. **The climb (DECIDED-4).** While two or more overloads share a suffix, each overload of every
   colliding set qualifies **every parameter's spelling one more level**, and the suffixes are
   recomputed; overloads that do not collide stay as they are. One level of a parameter spelling
   means:
   - a named type takes its next qualifier from **its own** chain — containing types nearest
     first, then namespace segments nearest first, then `Global` once the namespace chain is
     exhausted (`Numerics.Decimal` → `NumericsDecimal`; `Outer.Inner` → `OuterInner`;
     `A.B.Foo` → `BFoo` → `ABFoo` → `GlobalABFoo`);
   - a primitive never climbs: its CLR name (`Int32`, `UInt32`, `String`…) is already its full
     identity;
   - a generic type, array, nullable or tuple climbs its named components (the generic definition
     and each type argument, the element, the inner type) together, each from its own chain. A
     type argument climbs only because the whole parameter spelling collided — never on its own;
   - a type parameter spells its name and never climbs (`T`);
   - a part that names **the same declaration** (same assembly, same full name, same
     containing-type construction) at the same position in every overload of the colliding set cannot tell them apart, so it does not climb:
     `M(List<A.Id>)` / `M(List<B.Id>)` climbs `Id` only (`ListOfAId` / `ListOfBId`), and
     `K(Numerics.Decimal d, Shared.Foo f)` / `K(Other.Decimal d, Shared.Foo f)` gives
     `NumericsDecimalFoo` / `OtherDecimalFoo`;
   - a generic containing type's qualifier carries its type arguments (`Outer<int>.Inner` →
     `OuterOfInt32Inner`), so two constructions of it climb apart.
6. **Dead end (FR-024).** When a colliding set can climb no further (every component reached
   `Global`, which needs two types with the same full name from different assemblies, an extern
   alias, or two identical `[Name]` values), the core reports `MS0003` (`AmbiguousConstruct`,
   error) at the later overload and appends a deterministic positional marker to each overload of
   the set, in declaration order: `$1`, `$2`, … (`K(Lib1::N.Decimal d)` / `K(Lib2::N.Decimal d)` →
   `kGlobalNDecimal$1`, `kGlobalNDecimal$2`). The output stays valid TypeScript; the error fails
   the build. Other known dead ends: overloads that differ only in their generic arity
   (`M<T>(int x)` / `M(int x)` → `mInt32$1` / `mInt32$2`, TS2393 before), and the rare
   spellings the grammar does not separate (`Outer<A<B>, C>` / `Outer<A<B, C>>` both spell
   `OuterOfAOfBC`; a containing type's type arguments are spelled unqualified and do not climb,
   so `Outer<A.Id>.Inner` / `Outer<B.Id>.Inner` dead-end).
7. **`[Name]` (FR-005, OQ-1 (a)).** An overload carrying `[Name("…")]` — the attribute targeted
   at the emitting target, else the untargeted one — replaces its suffix with the attribute's
   value, capitalized, in **every** pass (step 1 and step 3 alike, an operator group included);
   the other overloads are named by the normal rules. The pin takes part in the distinctness
   checks like a computed suffix, but it is one part that never climbs: a collision with it moves
   the computed overloads (`Add(int x)` / `[Name("x")] Add(string s)` → `Int32` / `X`), and two
   equal pins, or a pin equal to a spelling that cannot climb (`[Name("int32")]` beside
   `F(int a)` once the group spells types), are a dead end (step 6). Only the suffix changes: the
   dispatcher, the operator helper and the module dispatcher keep the C# name (`[Name("cents")]`
   on `Money(int c)` → `byMoneyCents` / `fromCents`; on `Add(int c)` → `addCents` beside the
   dispatcher `add`; on `operator *(Money, int)` → `__multiplyCents` beside `__multiply`). A
   value that is not `^[A-Za-z_][A-Za-z0-9_]*$` (empty, a leading digit, `-`, `$`, which the
   claims and the dead end reserve) reports `MS0003` (error) at the attribute's argument, and the
   overload keeps its generated suffix. A member that is not an overload of a dispatched group —
   a single method or operator, or an overload of an `[External]`, `[JsCallable]` or
   `[PlainObject]` type, which emits no dispatcher — keeps today's meaning: `[Name]` renames the
   whole member. A conversion operator has no C# name, so its `[Name]` names its group and pins
   nothing (`[Name("toInt")] explicit operator int(Money)` / `explicit operator long(Money)` →
   `__toInt` over `__toIntMoneyToInt32` / `__toIntMoneyToInt64`). A `[Name]` on a constructor
   outside a dispatched set names nothing and reports an `MS0003` warning. Dart pins the same
   suffixes (slice 013g). Root anchoring decides overrides and implementations (§ Root anchoring):
   in a derived type's visible group an override is not named at all (the inherited overload it
   overrides stands for it) and an anchored implementation carries its root's pin, never its own,
   so an ignored `[Name]` cannot move the type's other overloads.
8. **Candidate names.** From the suffix `S` of overload `o` of type `T`:

   | Member | Candidate |
   |---|---|
   | selector | `by` + `T` + `S` — `T` is the declaring type's emitted simple name, so a derived class with a different name never redeclares an inherited selector (DECIDED-16, R2); a base and a derived type with the same simple name (`N1.Money : N2.Money`) are resolved by claim step 8 |
   | factory | `from` + `S`, or `from` + `T` + `S` under FR-026 |
   | method fast path | camelCased method name + `S` (`addXY`, `fInt32`, `nowDefault`) |
   | operator fast path | `__` + operator name + `S` (`__addMoneyInt32`) |
   | module fast path | camelCased function name + `S` (`twiceX`) |

9. **Determinism (FR-006).** Every step is a pure function of the declarations; iteration order is
   declaration order; distinctness is ordinal. Same source, same candidates (NFR-005).

## Spellings (Roslyn symbol, captured at extraction)

Exact CLR names; the legacy shortenings (`Int`, `Long`, `ULong`, `Bool`, `Float`) are retired
(DECIDED-3). `IrTypeRefMapper`'s folding (`uint` → `Int32`, `List<T>` → array, #475) does not reach
the spelling.

| C# type | Spelling | Example |
|---|---|---|
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` | `SByte`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64` | `U(int)` / `U(uint)` → `uInt32` / `uUInt32` |
| `nint`, `nuint` | `IntPtr`, `UIntPtr` | — |
| `float`, `double`, `decimal` | `Single`, `Double`, `Decimal` | — |
| `bool`, `char`, `string`, `object` | `Boolean`, `Char`, `String`, `Object` | — |
| `T[]` | `<Element>Array` | `int[]` → `Int32Array`; `Point[]` → `PointArray`; `int[][]` → `Int32ArrayArray` |
| `T?` (value or reference) | `<Inner>OrNull` | `int?` → `Int32OrNull`; `string?` → `StringOrNull` |
| `C<A, B>` (class, struct, interface, delegate) | `<C>Of<A><B>` | `List<int>` → `ListOfInt32`; `Dictionary<string, int>` → `DictionaryOfStringInt32`; `Func<int, bool>` → `FuncOfInt32Boolean` |
| `(A, B)` tuple | `TupleOf<A><B>` | `(int, string)` → `TupleOfInt32String` |
| `params T[]` | the array spelling | `params int[]` → `Int32Array` |
| `ref T` / `out T` / `in T` | `RefOf<T>` / `OutOf<T>` / `InOf<T>` (TS reports `MS0001` for `ref`/`out` today; defined for Dart) | — |
| named type `Outer.Inner` | `Inner`; climbs to `OuterInner` | step 5 |
| type parameter `T` | its name | `Identity<T>(T value)` / `Identity(string value)` → `identityT` / `identityString` |
| error / unresolved type | `Value` (today's fallback) | — |

## Root anchoring (DECIDED-15, FR-025)

- The candidate names of a method overload come from its **root declaration**: follow
  `IMethodSymbol.OverriddenMethod` to the first declaration (for an `override`), or take the
  implemented interface member (for an implicit or explicit interface implementation), using the
  member's `OriginalDefinition` for the names. The root group is the overload group of the root's
  declaring type (class or interface); its names are computed there, once, and every override and
  implementation reuses them. For an interface implementation the root is the **constructed**
  interface member (`IConv<int>.Conv(int x)`); a type that implements one generic interface more
  than once has two constructed roots per name and follows DECIDED-30 (runtime dispatch, `MS0069`).
  In claim step 7, a derived type's implementation of a construction its base did not implement
  (`D : C, IConv<string>` with `C : IConv<int>`, `D.Conv(string)`) is an **implementation** of a
  root, not a non-root candidate and not an override: it takes the root's name and joins the
  DECIDED-30 dispatcher for that name.
- **`[Name]` on an anchored overload (FR-005).** The root's `[Name]` names the root group, so
  every override and implementation inherits the pinned suffix (`IShape.[Name("side")]
  Area(int s)` → `areaSide` on the interface and on every implementer, whatever its parameter
  is called). The implementer's own `[Name]` cannot move a name its dispatcher and its callers
  share: when it names another suffix it is ignored and reported as an `MS0003` warning at the
  attribute (`[Name("ignored")] on 'Square.Area(int side)' is ignored: an override or interface
  implementation takes the suffix 'Side' of its root 'IShape.Area(int s)'; put [Name] on the
  root (spec 013 FR-005, FR-025).`); repeating the root's value is silent. A `new` method or a
  derived type's new overload is its own root, so its `[Name]` pins its suffix as usual.
- An override's fast path takes the root's mapped visibility (#445's root walk): a cross-assembly
  `protected override` of a `protected internal` root is public, as the root is (TS2415 otherwise,
  R14).
- A `new` (hiding) method, or a method that is neither an override nor an implementation, is its
  own root. Its candidate is a non-root candidate for the claim pass (step 7), so it can never
  silently override a base fast path (R14: `addX` → `addX$`).
- A derived type that declares **new** overloads next to inherited ones names them by running stage
  1 over the derived type's whole visible group: the inherited overloads it can see, then the
  members it declares, minus its overrides of an inherited overload. The inherited overload stands
  for its override, found by the `OverriddenMethod` chain and not by signature (a generic method's
  override declares its own type parameters), so a signature is named once and `override Add(int x)`
  next to a new `Add(bool b)` gives `addB` with no dead end (#564). An override that is emitted
  under another root's name than the overload it overrides — a multiply-constructed interface
  root, DECIDED-30: `QD : QB, IQ<int>, IQ<string>` with `IQ<T>.[Name("q")] Q(T a)`,
  `QB.[Name("bq")] Q(int z)`, `override Q(int z)` and a new `Q(bool q)` — is named in stage 1 by
  the base overload only (`Bq`), so the new overload computes the interface root's `qQ` and the
  claim pass moves it to `qQ$` with `MS0067` (it was `qBoolean` while the override was named
  twice). The inherited overloads
  keep their root names; the new ones take the names computed for them and claim them after the
  inherited names, against inherited instance members of every visibility (§ Claim pass, step 7).
  The derived type emits an `override` dispatcher whose signatures are a **superset** of the base
  dispatcher's — including base overloads it cannot see (`private`, or `internal` to another
  assembly), tagged as #445 tags them — whose branches for its own overloads call their fast
  paths, and whose unmatched calls forward to `super.<dispatcher>(...args)` (TS2416 otherwise, R14).
- One method that implements several roots whose names differ (two interfaces, or a base override
  plus an interface) emits its body under the first root's name in declaration order of the base
  list, and each further name as a forwarding member (`areaB(b: number) { return this.areaA(b); }`).

## Claim pass (each target bridge, DECIDED-18)

**Scopes.** TypeScript: per class, one static table and one instance table; per module group, the
namespace barrel (all modules re-exported by one `index.ts`, R11). Dart: one table per class shared
by instance members, static members and named constructors; one per library for top-level
functions.

**Order** (each category in declaration order; earlier claims win):

1. user-written members (never renamed, including user statics and the names of user overloads'
   dispatchers, which are the C# member names);
2. dispatchers (constructor and method dispatchers keep their C# names);
3. selectors;
4. factories;
5. method and module fast paths (the root group's names first, then a derived type's new
   overloads);
6. operator fast paths;
7. inherited instance members: every **non-root** instance candidate (a `new` method's, a derived
   type's new overload's) claims against the instance members of every emitted base class, of
   every visibility (`private` included: TypeScript checks private members for assignability,
   TS2416). Root names that an override reuses are not claimed here: they are meant to override;
8. inherited statics: a selector, factory or static fast-path candidate equal to an inherited
   static's name is checked here; a factory that meets FR-026's conditions is kept
   (`static override`), one that does not is qualified, and anything still taken (a selector of a
   same-named base type, a static fast path, since TypeScript checks every static for
   assignability, TS2417) gets `$`. A qualified factory stays qualified when its suffix takes `$`
   (`fromIntBoxValue$`). Inherited factories are compared in the deriving class's type
   parameters, rebound through each `extends` clause's arguments against the base's declared
   type parameters, level by level.

**Per-suffix claims.** A claim resolves the overload's suffix, not one name: when any candidate
derived from `S` (the selector and the factory of a constructor overload) is taken, `S` becomes
`S$` (then `S$$`, …) and every candidate of that overload is recomputed, so the factory and the
selector of one overload always share one spelling (`fromCents$` / `byCashCents$` is never
split). Fast paths have one candidate each, so the rule reduces to `name$`.

**Reporting.** Every rename reports `MS0067` (FR-020): reason 1 when a user member claimed the
name, reason 2 when an earlier synthesized name did, reason 3 when an inherited instance member
did (step 7). A user member that would **override** an inherited fast path, and a user **static**
that would redeclare an inherited generated factory (`Receipt.FromAmount(string)` against
`Money.fromAmount(amount: Decimal)`, TS2417), report `MS0068` instead (FR-034): user members are
never renamed, so the build fails and points at `[Name]`.

**Metadata-only roots (DECIDED-28).** A root in a DLL or package is named by running the
producer's whole pipeline — stage 1 and this claim pass — over the metadata symbols. Implementation
DLLs keep private members, so the claims match; reference assemblies strip them, and a rename the
consumer then misses surfaces as TS4117 on the consumer's `override` (R14).

**Identity, not strings.** A direct call site carries the overload's root identity
(`IrOverloadRef`, data-model.md § 2). The bridge resolves it through the declaring type's claim
results, computed once per type and cached, so a call in another file or package spells exactly
what the declaration emitted.

## FR-026 — factory names against inherited statics

A factory candidate `from<S>` of type `T` keeps its name unless an emitted base class (at any
depth) has a static member of the same TypeScript name. Then:

- if that inherited member is a generated factory that is **not `private`**, whose overload has
  **the same parameter types in the same order, with the same `params` / optional shape** —
  compared on the **constructed** base constructor (for `Box2<T> : Box<T>`, `Box<T>`'s `T` is
  `Box2`'s `T`; for `IntBox : Box<int>` the `extends` clause fixes `T`, and an inherited factory
  that stays generic (`fromValue<T>(value: T): Box<T>`) can never be redeclared by a non-generic
  one, so `IntBox`'s is qualified, `fromIntBoxValue`) — and whose accessibility `T`'s factory
  matches or widens (public ≥ protected), and — for a generic class — whose type parameters, on
  the same constructed mapping, carry **the same constraints** as the base's (`SortedBox<T> :
  Box<T> where T : IComparable<T>` adds a constraint, so TypeScript's static side rejects the
  kept `fromValue<T extends Comparable<T>>`: TS2417, R15; a different type-parameter count is
  fine, `Pair<K, V> : Box<K>` keeps `fromValue`), then `T`'s factory keeps `from<S>` and is emitted
  `static override` (TS4114 under `noImplicitOverride` otherwise, R14): same parameters, a
  covariant return type and no narrowing keep the static side compatible (R2:
  `Receipt.fromAmount`, `GiftReceipt.fromCents`);
- otherwise — a different parameter list, a `private` base factory (TS2417: a private member
  cannot be redeclared, R14 / reviewer p2), a narrower derived accessibility (a `protected`
  factory against a public one, R14 / reviewer p3), different type-parameter constraints (R15),
  or an inherited user static — the factory becomes `from` + `T` + `S` (R2:
  `GiftReceipt.fromGiftReceiptAmount`; R14: `Receipt.fromReceiptCode`, `Receipt.fromReceiptId`;
  R15: `SortedBox.fromSortedBoxValue`). The qualified name then goes through the
  claim pass like any other.

The rule reads declarations only (parameter types, accessibility and type-parameter constraints
on the constructed mapping), so it is deterministic. It is a conservative stand-in for
TypeScript's static-side assignability check, not a replacement for it: for generic classes that
check also compares constraints, which is why constraints are part of the rule (R15). The reverse direction — a derived **user** static against an inherited generated factory —
cannot be qualified (user names never move) and is `MS0068`.

## Worked examples

| C# | Today | 013 |
|---|---|---|
| `Add(int x, int y)` / `Add(Point p)` | `addXY`, `addP` | unchanged |
| `F(int a)` / `F(string a)` | `fInt`, `fString` | `fInt32`, `fString` |
| `G(int v)` / `G(long v)` | `gInt`, `gLong` | `gInt32`, `gInt64` |
| `U(int a)` / `U(uint a)` | `uInt`, `uInt` (TS2393) | `uInt32`, `uUInt32` |
| `W(long a)` / `W(ulong a)` | `wLong`, `wULong` | `wInt64`, `wUInt64` |
| `L(List<int> xs)` / `L(int[] xs)` | `lIntArray`, `lIntArray` (TS2393) | `lListOfInt32`, `lInt32Array` |
| `Sum(int[] xs)` / `Sum(double[] xs)` | `sumIntArray`, `sumDoubleArray` | `sumInt32Array`, `sumDoubleArray` |
| `H(int? maybe)` / `H(string? maybe)` | `hIntOrNull`, `hStringOrNull` | `hInt32OrNull`, `hStringOrNull` |
| `K(Numerics.Decimal d)` / `K(Other.Decimal d)` | `kDecimal`, `kDecimal` (TS2393) | `kNumericsDecimal`, `kOtherDecimal` |
| `K(Numerics.Decimal d, int n)` / `K(Other.Decimal d, int n)` | — | `kNumericsDecimalInt32`, `kOtherDecimalInt32` (`Int32` does not climb) |
| `M(List<A.Id> ids)` / `M(List<B.Id> ids)` | — | `mListOfAId`, `mListOfBId` (the type argument climbs because the whole spelling collided) |
| `Now()` / `Now(int tick)` | `now`, `nowTick` (TS2393 + TS2385) | `nowDefault`, `nowTick` |
| `Format(string fmt, int count)` / `Format(string fmt, params object[] args)` | `formatFmtCount`, `formatFmtArgs` | unchanged |
| `Identity<T>(T value)` / `Identity(string value)` | `identityValue<T>`, `identityString` | `identityT<T>`, `identityString` |
| `Money(decimal amount)` / `Money(int cents)` | dispatcher branches only | selectors `byMoneyAmount` / `byMoneyCents`, factories `fromAmount` / `fromCents` |
| `Receipt : Money` with `Receipt(decimal amount)` / `Receipt(int cents)` | — | `byReceiptAmount` / `byReceiptCents`; `fromAmount` / `fromCents` kept (FR-026, same lists) |
| `GiftReceipt : Receipt` with `GiftReceipt(string amount)` / `GiftReceipt(int cents)` | — | `fromGiftReceiptAmount` (FR-026, different list) / `fromCents` |
| `Cash(decimal amount)` / `Cash(int cents)` + `static From(decimal amount)` / `From(int cents)` | private `fromAmount`, `fromCents` fast paths beside no factory | factories `fromAmount` / `fromCents`; fast paths `fromAmount$` / `fromCents$` + `MS0067` reason 2 (R11) |
| `virtual Add(int x)` / `Add(string s)`; `Derived` overrides `Add(int x)` | `Derived.add(x)` replaces the dispatcher (TS2416, R8) | `Base.addX` / `addS`; `Derived` emits `override addX` only |
| `Derived : Base` with `new Add(int x)` (hiding) | — | `addX$` + `MS0067` reason 3 (R14) |
| `Base.Add(int x)` + `private Add(string s)`; `Derived.Add(short s)` | — | `addS$` + `MS0067` reason 3; `Derived` emits the superset `override add` (R14) |
| `Money` private `Money(string code)`; `Receipt(string code)` | — | `Receipt.fromReceiptCode` (FR-026: private base factory) |
| `IShape.Area(int s)` / `Area(int w, int h)`; `Sq.Area(int side)` | `Sq.areaSide`, `areaWH` (private) | `areaS`, `areaWH` on `IShape` and `Sq` (root names) |
| extension `Twice(this Base b, int x)` / `Twice(this Base b, string s)` | two `export function twice` (TS2393) | `twiceX`, `twiceS` + dispatcher `twice` |
| operators `+(Acct a, long b)` / `+(Acct a, ulong b)` | `__addAcctLong`, `__addAcctULong` | `__addAcctInt64`, `__addAcctUInt64` |
| `Money(string text)` / `[Name("cents")] Money(int value)` | `[Name]` not allowed on a constructor (CS0592) | `fromText` / `fromCents`, `byMoneyText` / `byMoneyCents` (FR-005) |
| `Add(int x)` / `[Name("x")] Add(string s)` / `[Name(TargetLanguage.TypeScript, "flag")] Add(bool b)` | `[Name]` ignored | `addInt32` / `addX` / `addFlag` (the pin collides, the others spell types) |
| `[Name("v")] Put(int a)` / `[Name("v")] Put(string b)` | — | `putV$1` / `putV$2` + `MS0003` (dead end) |

## Stability (D7)

- Renaming a C# parameter renames the factory, the selector and the fast path.
- Adding one overload can flip the whole group from pass 1 to pass 2 (or into a climb).
- Moving a **parameter type** between namespaces or containing types can rename a climbed suffix.
- Renaming a type renames its selectors (`by<TypeName>…`) and any FR-026-qualified factory.
- `[Name]` pins a suffix against all of the above except the type-name prefixes.
