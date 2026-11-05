# Contract: Diagnostics `MS0067` and `MS0068`

**Normative for**: 013 FR-004, FR-005, FR-020, FR-024, FR-034. This spec PR reserves `MS0067` and
`MS0068` in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md);
`MS0066` is the last code allocated before it (spec 012). The climb's dead end reuses `MS0003`.
Direct call sites that cannot be resolved (a metadata-only reference, `dynamic`, unresolved
symbols) report nothing: they fall back to the dispatcher, which is correct, only slower (FR-019).

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0067` | `OverloadNameShadowed` | Warning | the overload whose candidate name was `$`-renamed |
| `MS0068` | `OverloadFastPathHijacked` | Error | the user member that would override an inherited generated member |
| `MS0003` (existing) | `AmbiguousConstruct` | Error | the later overload of a set the climb cannot separate (FR-024) |

## `MS0067` reasons

The claim pass of naming.md renamed a factory, a selector or a fast path (per suffix) because its
candidate was already taken in the same scope:

1. by a **user-written member** (a class member, a namespace / module function, or a user static
   inherited from an emitted base);
2. by an **earlier synthesized name** in the claim order — for example the `From(decimal amount)` /
   `From(int cents)` fast paths `fromAmount` / `fromCents`, claimed by the factories of
   `Cash(decimal amount)` / `Cash(int cents)` (R11).

Examples:

- `Factory 'Money.fromCents' collides with the user member 'fromCents'; the generated factory and selector are emitted as 'fromCents$' and 'byMoneyCents$' (spec 013 FR-004).`
- `Fast path 'Cash.fromAmount' of 'From(decimal amount)' collides with the factory of 'Cash(decimal amount)'; it is emitted as 'fromAmount$' (spec 013 FR-004).`

One report per renamed overload, naming the candidate, the claimant and the emitted name. The
warning is what makes `$` acceptable on public API: nothing changed in the C# source, so the build
must say so.

## `MS0068` reason

A user-written member of a derived type (or of a class implementing an interface) has the
TypeScript name of a **fast path inherited from a root group** that the member does not override
(for example a C# method `AddX()` in a class whose base declares `Add(int x)` / `Add(string s)`).
In TypeScript the member would override the fast path, and the inherited dispatcher, which calls
`this.addX`, would run the user member instead. The user member cannot be renamed (it is user API)
and the root's name is fixed (it may live in another package), so the build fails:

`'Derived.AddX' would override the generated fast path 'addX' of 'Base.Add(int x)'; rename the member, or rename the root overload's parameter or pin it with [Name] (spec 013 FR-034).`

## `MS0003` (dead end)

`Overloads 'K(Lib1::N.Decimal d)' and 'K(Lib2::N.Decimal d)' have the same naming spelling at every qualification level; they are emitted as 'kGlobalNDecimal$1' and 'kGlobalNDecimal$2' (spec 013 FR-024).`

The positional markers keep the output valid TypeScript; the error fails the build.
