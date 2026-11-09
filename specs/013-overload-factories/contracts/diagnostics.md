# Contract: Diagnostics `MS0067`, `MS0068` and `MS0069`

**Normative for**: 013 FR-004, FR-005, FR-020, FR-024, FR-034, FR-036. This spec PR reserves `MS0067`,
`MS0068` and `MS0069` in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md);
`MS0066` is the last code allocated before it (spec 012). The climb's dead end reuses `MS0003`.
Direct call sites that cannot be resolved (a metadata-only reference, `dynamic`, unresolved
symbols) report nothing: they fall back to the dispatcher, which is correct, only slower (FR-019).

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0067` | `OverloadNameShadowed` | Warning | the overload whose candidate name was `$`-renamed |
| `MS0068` | `OverloadFastPathHijacked` | Error | the user member that would override an inherited fast path, or redeclare an inherited factory |
| `MS0069` | `MultiplyConstructedOverloadGroup` | Warning | the type implementing one generic interface more than once |
| `MS0003` (existing) | `AmbiguousConstruct` | Error | the later overload of a set the climb cannot separate (FR-024); an invalid `[Name]` pin's argument (FR-005) |
| `MS0003` (existing) | `AmbiguousConstruct` | Warning | the `[Name]` argument of an override or implementation that is ignored for its root's name (FR-005, FR-025), or of a constructor outside a dispatched set (FR-005) |

## `MS0067` reasons

The claim pass of naming.md renamed a factory, a selector or a fast path (per suffix) because its
candidate was already taken in the same scope:

1. by a **user-written member** (a class member, a namespace / module function, or a user static
   inherited from an emitted base);
2. by an **earlier synthesized name** in the claim order — for example the `From(decimal amount)` /
   `From(int cents)` fast paths `fromAmount` / `fromCents`, claimed by the factories of
   `Cash(decimal amount)` / `Cash(int cents)` (R11);
3. by an **inherited instance member** of any visibility (claim step 7), for a non-root candidate —
   a `new` (hiding) method's `addX` against the base fast path, or a derived overload's `addS`
   against a base `private addS` (R14).

Examples:

- `Factory 'Money.fromCents' collides with the user member 'fromCents'; the generated factory and selector are emitted as 'fromCents$' and 'byMoneyCents$' (spec 013 FR-004).`
- `Fast path 'Cash.fromAmount' of 'From(decimal amount)' collides with the factory of 'Cash(decimal amount)'; it is emitted as 'fromAmount$' (spec 013 FR-004).`

One report per renamed overload, naming the candidate, the claimant and the emitted name. The
warning is what makes `$` acceptable on public API: nothing changed in the C# source, so the build
must say so.

## `MS0068` reasons

Reason 1: a user-written member of a derived type (or of a class implementing an interface) has the
TypeScript name of a **fast path inherited from a root group** that the member does not override
(for example a C# method `AddX()` in a class whose base declares `Add(int x)` / `Add(string s)`).
In TypeScript the member would override the fast path, and the inherited dispatcher, which calls
`this.addX`, would run the user member instead. The user member cannot be renamed (it is user API)
and the root's name is fixed (it may live in another package), so the build fails:

`'Derived.AddX' would override the generated fast path 'addX' of 'Base.Add(int x)'; rename the member, or rename the root overload's parameter or pin it with [Name] (spec 013 FR-034).`

Reason 2 (maintainer decision, 2026-11-05): a **user static** of a derived type has the name of
an inherited generated factory (`Receipt.FromAmount(string)` against `Money.fromAmount(amount:
Decimal)`); TypeScript checks the static side (TS2417, R14), and neither name can move:

`'Receipt.FromAmount' would redeclare the generated factory 'fromAmount' of 'Money(decimal amount)'; rename the member or give it [Name], or pin the constructor's suffix with [Name] (spec 013 FR-034).`

## `MS0069` reason

A type implements one generic interface with overload groups more than once (`C : IConv<int>,
IConv<string>`). Each root fast-path name would need two implementations (TS2393 / TS2416, R14),
so the type keeps runtime dispatch for those names and gets no per-implementation fast paths
(DECIDED-30):

`'C' implements 'IConv<T>' more than once; calls to 'Conv' keep runtime dispatch (spec 013 FR-036).`

When two constructions are indistinguishable at run time (`IConv<List<int>>` and `IConv<int[]>`, both arrays), the message adds: `the constructions 'IConv<List<int>>' and 'IConv<int[]>' cannot be told apart at run time; the first branch wins.` — the same outcome as today's dispatcher.

## The Dart target (slice 013g)

Dart claims in one table per class (dart-mapping.md), so it reports `MS0067` and `MS0068` from
its own claim pass with the same codes, severities and locations, and messages prefixed with
`Dart target:` (`DartClassClaimPass`, `DartFlatClaimPass`):

`Dart target: Named constructor 'Money.fromCents' of 'Money(int c)' collides with the user member 'fromCents'; it is emitted as 'fromCents$' (spec 013 FR-004).`

`Dart target: Fast path 'Dog.speakN' of 'Speak(int n)' collides with the fast path of 'Speak(int n)' inherited from 'Animal'; it is emitted as 'speakN$' (spec 013 FR-004).`

`Dart target: 'Parrot.speakN' would override the fast path of 'Speak(int n)' inherited from 'Animal'; rename the member, or rename the root overload's parameter or pin it with [Name] (spec 013 FR-034).`

`MS0069` is the core's diagnostic, reported by the Dart class emission as by TypeScript's; Dart
declares the first implementation of a shared root name (a Dart class cannot implement two
constructions of one interface either). `MS0068` reason 2 does not arise: Dart constructors are
not inherited.

## `MS0003` (dead end)

`Overloads 'K(N.Decimal d)' and 'K(N.Decimal d)' have the same naming spelling at every qualification level; they are named with the suffixes 'GlobalNDecimal$1' and 'GlobalNDecimal$2' (spec 013 FR-024).`

The core names suffixes, not emitted members: each target derives and claims the member names (DECIDED-18).

The positional markers keep the output valid TypeScript; the error fails the build.

When `[Name]` pins a suffix of the set (FR-005), the message says so:

`Overloads 'Put(int a)' and 'Put(string b)' have the same suffix at every qualification level, and [Name] pins the suffix of 'Put(int a)' and 'Put(string b)'; they are named with the suffixes 'Value$1' and 'Value$2' (spec 013 FR-005, FR-024).`

## `MS0003` (`[Name]` on an overload, FR-005)

An invalid pin — not `^[A-Za-z_][A-Za-z0-9_]*$` — is an error at the attribute's argument; the
overload keeps its generated suffix:

`[Name("2nd")] on the overload 'Put(string s)' is not a valid suffix: it must start with an ASCII letter or '_' and contain only ASCII letters, digits and '_'; the overload keeps its generated suffix (spec 013 FR-005).`

A `[Name]` on an override or interface implementation that names another suffix than its root's
is ignored, a warning at the attribute's argument (FR-025):

`[Name("ignored")] on 'Square.Area(int side)' is ignored: an override or interface implementation takes the suffix 'Side' of its root 'IShape.Area(int s)'; put [Name] on the root (spec 013 FR-005, FR-025).`

A `[Name]` on a constructor that is not part of a dispatched set names no factory or selector,
a warning at the attribute's argument:

`[Name("cents")] on the constructor 'Fee(int value)' has no effect: only an overload of a dispatched constructor set gets a factory and a selector to name (spec 013 FR-005).`

The Dart target reports the same `[Name]` diagnostics since slice 013g: it pins the same suffixes.
