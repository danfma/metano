# Contract: Import completeness for companion-namespace variants

This is the behavioral contract the fix must satisfy. It is target-facing (the "interface" a compiler exposes is its generated output) and is verified by the golden test plus the Vigiata regeneration.

## Invariant

> For any generated `.ts` file, every external symbol referenced by any node in the file — **including nodes inside a `TsNamespaceDeclaration.Members` companion block, at any depth** — has a corresponding `import` in that file, and no referenced symbol is imported more than once.

Two symbol classes, two ownership paths:

| Symbol class | Examples | Owned by | Rule |
|--------------|----------|----------|------|
| Referenceable names | intra-project types, cross-package types, value refs (`new T`, `instanceof T`), guards, extension helpers | `ImportCollector` (TS target) | Collected by walking `Functions` **and** `Members` of every namespace, recursively |
| Runtime helpers | `valueEquals`, `HashCode`, `delegateAdd`, type-check helpers, … | `IrRuntimeRequirementScanner` (core) | Collected by scanning the top-level type **and** its `NestedTypes`, recursively |

## Conformance cases

### C1 — intra-project type used only in a variant (defect 1)

**Input** (single namespace):
```csharp
[Name("UserProfile")]
public sealed record UserProfileDto(string Id);

[Name("GetUserProfileResponse")]
[StrictUnionGuard]
public abstract record GetUserProfileResponseData
{
    public sealed record Unauthorized : GetUserProfileResponseData;
    public sealed record UserProfileLoaded(UserProfileDto UserProfile) : GetUserProfileResponseData;
}
```

**Expected** (variant file): imports include the renamed type from its own file, e.g.
```ts
import { UserProfile } from "<intra-project path>/user-profile";
```
and the file type-checks (no `TS2304: Cannot find name 'UserProfile'`).

### C2 — non-strict field in a variant (defect 2)

For the same input, the variant file's runtime imports include `valueEquals` next to `HashCode`:
```ts
import { HashCode, valueEquals } from "metano-runtime";
```
and the file type-checks (no `TS2304: Cannot find name 'valueEquals'`).

### C3 — no spurious helper import (negative control, FR-005)

**Input**: a variant whose only field is primitive/strict, e.g. `Loaded(int Count)`.
**Expected**: the variant file imports `HashCode` (record) but **does not** import `valueEquals`.

### C4 — dedup (SC-005)

A symbol referenced both at the top level and inside a variant (e.g., `HashCode`) appears in **exactly one** import statement. Runtime requirements collapse via the requirement `HashSet`; named imports collapse via the existing collector dedup.

### C5 — no regression for files without nested variants

A file with no `TsNamespaceDeclaration.Members` and no nested types produces byte-identical output to before the fix (verified by the unchanged existing golden suite).

### C6 — cross-package / value-ref / guard used only in a variant (generality, US2)

A variant referencing a type from another `[EmitPackage]` assembly, a `new`/`instanceof` value reference, or a generated guard — used nowhere else in the file — emits the corresponding import via the same `Members` recursion as C1.

## Acceptance oracle

- **Golden test**: `tests/Metano.Tests/` asserts the full expected `.ts` for C1+C2 (and the negative control C3). Must fail pre-fix, pass post-fix.
- **External type-check**: regenerated Vigiata `get-user-profile-response.ts` passes `tsc --noEmit` with zero `TS2304` (SC-001/SC-004).
- **Suite**: full TUnit suite + all sample `bun` builds remain green (SC-003).
