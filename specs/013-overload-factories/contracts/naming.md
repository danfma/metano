# Contract: The overload naming algorithm

**Normative for**: 013 FR-001–FR-006, FR-024–FR-026, FR-032, FR-033. One algorithm names the
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
   pass (`Twice(this Base b, int x)` / `Twice(this Base b, string s)` → `twiceX` / `twiceS`, R11;
   `Twice(this Base b, int x)` / `Twice(this Other o, int x)` → `twiceBX` / `twiceOX`).
2. **Distinctness, pass 1.** If every suffix in `G` is distinct (ordinal), done.
3. **Suffix, pass 2 — type spellings.** On any collision, **every** overload of `G` switches to the
   § Spellings of its parameter types, concatenated in order (`F(int a)` / `F(string a)` →
   `Int32` / `String`). Operator groups start here: their suffix is always the type spellings
   (FR-033, today's convention).
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
   - a type parameter spells its name and never climbs (`T`).
6. **Dead end (FR-024).** When a colliding set can climb no further (every component reached
   `Global`, which needs two types with the same full name from different assemblies, an extern
   alias, or two identical `[Name]` values), the core reports `MS0003` (`AmbiguousConstruct`,
   error) at the later overload and appends a deterministic positional marker to each overload of
   the set, in declaration order: `$1`, `$2`, … (`kGlobalDecimal$1`, `kGlobalDecimal$2`). The output
   stays valid TypeScript; the error fails the build.
7. **`[Name]` (FR-005, OQ-1).** An overload carrying `[Name("…")]` replaces its suffix with the
   attribute's value, capitalized, before step 2; it takes part in the distinctness checks like a
   computed suffix. An invalid identifier reports `MS0003`.
8. **Candidate names.** From the suffix `S` of overload `o` of type `T`:

   | Member | Candidate |
   |---|---|
   | selector | `by` + `T` + `S` — `T` is the declaring type's emitted simple name, so a derived class never redeclares an inherited selector (DECIDED-16, R2) |
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
  member's `OriginalDefinition`. The root group is the overload group of the root's declaring type
  (class or interface); its names are computed there, once, and every override and implementation
  reuses them.
- A `new` (hiding) method, or a method that is neither an override nor an implementation, is its
  own root.
- A derived type that declares **new** overloads next to inherited ones names them by running stage
  1 over the derived type's whole visible group (inherited + declared). The inherited overloads
  keep their root names; the new ones take the names computed for them, claimed after the inherited
  names (§ Claim pass).
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
7. inherited statics: a selector or factory candidate equal to an inherited static's name is
   checked here; a factory that hits FR-026 is qualified instead of claimed.

**Per-suffix claims.** A claim resolves the overload's suffix, not one name: when any candidate
derived from `S` (the selector and the factory of a constructor overload) is taken, `S` becomes
`S$` (then `S$$`, …) and every candidate of that overload is recomputed, so the factory and the
selector of one overload always share one spelling (`fromCents$` / `byCashCents$` is never
split). Fast paths have one candidate each, so the rule reduces to `name$`.

**Reporting.** Every rename reports `MS0067` (FR-020): reason 1 when a user member claimed the
name, reason 2 when an earlier synthesized name did. A user member that would **override** an
inherited fast path reports `MS0068` instead (FR-034): it cannot be renamed, and silently
overriding the fast path would hijack the inherited dispatcher.

**Identity, not strings.** A direct call site carries the overload's root identity
(`IrOverloadRef`, data-model.md § 2). The bridge resolves it through the declaring type's claim
results, computed once per type and cached, so a call in another file or package spells exactly
what the declaration emitted.

## FR-026 — factory names against inherited statics

A factory candidate `from<S>` of type `T` keeps its name unless an emitted base class (at any
depth) has a static member of the same TypeScript name. Then:

- if that inherited member is a generated factory whose overload has **the same parameter types
  in the same order, with the same `params` / optional shape** (compared on the Roslyn symbols),
  `T`'s factory keeps `from<S>`: same parameters and a covariant return type keep the static side
  compatible (R2: `Receipt.fromAmount`, `GiftReceipt.fromCents`);
- otherwise — a different parameter list, a generic base factory whose type parameter `T`'s
  overload replaces with a concrete type, or an inherited user static — the factory becomes
  `from` + `T` + `S` (R2: `GiftReceipt.fromGiftReceiptAmount`). The qualified name then goes
  through the claim pass like any other.

The rule reads declarations only, so it is deterministic and needs no TypeScript assignability
check; it qualifies a compatible pair only in the generic-base case, which is the conservative
direction.

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
| `IShape.Area(int s)` / `Area(int w, int h)`; `Sq.Area(int side)` | `Sq.areaSide`, `areaWH` (private) | `areaS`, `areaWH` on `IShape` and `Sq` (root names) |
| extension `Twice(this Base b, int x)` / `Twice(this Base b, string s)` | two `export function twice` (TS2393) | `twiceX`, `twiceS` + dispatcher `twice` |
| operators `+(Acct a, long b)` / `+(Acct a, ulong b)` | `__addAcctLong`, `__addAcctULong` | `__addAcctInt64`, `__addAcctUInt64` |

## Stability (D7)

- Renaming a C# parameter renames the factory, the selector and the fast path.
- Adding one overload can flip the whole group from pass 1 to pass 2 (or into a climb).
- Moving a **parameter type** between namespaces or containing types can rename a climbed suffix.
- Renaming a type renames its selectors (`by<TypeName>…`) and any FR-026-qualified factory.
- `[Name]` pins a suffix against all of the above except the type-name prefixes.
