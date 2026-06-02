# Contract: Diagnostics

**Covers**: FR-005, FR-010, SC-004

Codes start at **MS0027** — `MS0026` is reserved by the in-flight JSX feature on branch `002` (not yet merged); skipping it avoids a merge collision.

## MS0027 — `InvalidJsTuple`

**Severity**: Error. **Location**: the `[JsTuple]` type declaration (no-positional-shape case), or the offending member (extra-member case).

A `[JsTuple]` record lowers to a **bare JS array tuple** (`[T0, T1, …]`), so it must be **positional-only**. `MS0027` is raised when either invariant breaks:

1. **No positional shape** — applied to a type with no primary-constructor parameter list (a non-positional record, or a class/struct without a positional constructor). There is no field order to map to array slots.
2. **Extra member** — a `[JsTuple]` record declares an instance member beyond its positional elements. The array tuple has no object to host the member; a `p.Sum`-style access would read `undefined` at runtime. The record's synthesized value-semantics members (`Equals`/`GetHashCode`/`ToString`/`Deconstruct`/`<Clone>$`/copy-ctor/`EqualityContract`) and the positional properties are exempt.

| Input | Expected |
|-------|----------|
| `[JsTuple] record Bad { public int X { get; init; } }` | `MS0027` at `Bad` (no positional shape) |
| `[JsTuple] record Pair(int A, int B) { public int Sum => A + B; }` | `MS0027` at `Sum` (extra member) |
| `[JsTuple] record Good(int A, int B);` | no diagnostic |

## MS0028 — `InvalidJsCallable`

**Severity**: Error. **Location**: the type or the offending member.

Raised when `[JsCallable]` is applied to a non-interface, or a `[JsCallable]` interface exposes any member other than `Invoke` — **declared OR inherited from a base interface**. The whole interface surface is erased to a single JS callable, so a non-`Invoke` member anywhere in the hierarchy has no place to live.

| Input | Expected |
|-------|----------|
| `[JsCallable] class C { }` | `MS0028` at `C` (non-interface) |
| `[JsCallable] interface I { void Invoke(int x); int Other(); }` | `MS0028` at `Other` |
| `[JsCallable] interface I : IBase { void Invoke(int x); }` where `IBase` has a non-`Invoke` member | `MS0028` at the inherited member |
| `[JsCallable] interface I { void Invoke(int x); void Invoke(string s); }` | no diagnostic (overloaded Invoke allowed) |

## Invariants
- No silently-wrong output on misuse — a diagnostic is raised instead (Constitution V).
- Codes added to `Diagnostics/MetaSharpDiagnostic.cs` and the baseline `diagnostic-catalog.md`.
