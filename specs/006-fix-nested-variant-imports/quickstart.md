# Quickstart: Reproduce & validate the nested-variant import fix

## Prerequisites

- .NET 10 SDK (`dotnet`), Bun, and a checkout of Metano.
- Optional for the end-to-end check: the Vigiata working copy at `../../Vigiata` relative to the Metano repo (already present in this environment).

## 1. Reproduce the bug (before the fix)

The minimal reproduction is an abstract record with `[StrictUnionGuard]` and a payload variant referencing another renamed record in the same namespace:

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

Confirmed against the real consumer (Vigiata). Regenerate and type-check:

```sh
# from the Metano repo root
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p ../../Vigiata/src/Vigiata.Contracts/Vigiata.Contracts.csproj \
  -o ../../Vigiata/frontend/vigiata-app/src/apis/vigiata-contracts --clean

cd ../../Vigiata/frontend/vigiata-app && ./node_modules/.bin/tsc --noEmit -p tsconfig.json \
  2>&1 | grep -E "get-user-profile-response|UserProfile|valueEquals"
```

Expected **before the fix**:
```
get-user-profile-response.ts(..): error TS2304: Cannot find name 'UserProfile'.
get-user-profile-response.ts(..): error TS2304: Cannot find name 'valueEquals'.
```

## 2. Apply the fix

Two edits (see plan.md / research.md for exact rationale):

1. **Core** — `src/Metano.Compiler/Extraction/IrClassExtractor.cs:84`: populate `NestedTypes` with the transpilable nested types (same `SymbolHelper.IsTranspilable` gate as discovery) instead of `null`.
2. **TS target** — `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs:741-749`: in the `TsNamespaceDeclaration` case, recurse `CollectFromTopLevel` over `ns.Members` and route `ns.Functions` through the same entry point.

## 3. Validate (after the fix)

```sh
# C# format + build + golden tests
dotnet csharpier .
dotnet build
dotnet run --project tests/Metano.Tests/        # new NestedRecordVariant golden must pass

# Regenerate Vigiata and re-type-check — must be clean now
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p ../../Vigiata/src/Vigiata.Contracts/Vigiata.Contracts.csproj \
  -o ../../Vigiata/frontend/vigiata-app/src/apis/vigiata-contracts --clean
cd ../../Vigiata/frontend/vigiata-app && ./node_modules/.bin/tsc --noEmit -p tsconfig.json
```

Expected **after the fix**:
- The new golden test passes; it fails when reverting either edit.
- The variant file imports the referenced type and `valueEquals` (next to `HashCode`).
- `tsc --noEmit` reports **zero** `TS2304` for the contracts output.

## 4. Regression sweep

```sh
dotnet run --project tests/Metano.Tests/                 # full TUnit suite green
cd targets/js/sample-issue-tracker && bun run build && bun test
cd targets/js/sample-todo && bun run build && bun test
# plus any other sample whose source uses nested record variants
```

## Done criteria (maps to Success Criteria)

- SC-001/SC-004: contracts output type-checks, zero "cannot find name".
- SC-003: full TUnit suite + sample builds green, no regressions.
- SC-004: new golden fails pre-fix, passes post-fix.
- SC-005: no duplicate imports; no `valueEquals` for the strict-only negative control.
