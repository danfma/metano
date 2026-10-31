# Implementation Plan: Opt-in exports subpath root

**Branch**: `feat/267-exports-subpath-root` | **Date**: 2026-10-31 | **Spec**: [spec.md](./spec.md)

## Summary

Add `SubpathRoot` to `[EmitPackage]`, validate it once into the core value object
`PackageSubpathRoot`, carry it through the frontend (`EmitPackageInfo`,
`IrCompilation.PackageSubpathRoot`, `IrTypeOrigin.PackageSubpathRoot`), and apply one
TypeScript-side mapping (`PackageExportSubpaths`) in three places: the transform-time
collision check, the `package.json#exports` key builder, and the cross-package subpath builder
(type origins and `[NoContainer]` exports).

## Technical Context

C# 14 / .NET 10, Roslyn 5.3, TUnit. TypeScript target only; Dart ignores the new IR field.

## Constitution Check

| Principle | Status | Notes |
|---|---|---|
| Spec as source of truth | ✅ | This spec amends 005 FR-002/FR-013 before implementation. |
| Clean Code / expressive names | ✅ | One value object (`PackageSubpathRoot`) owns validation; one static mapper (`PackageExportSubpaths`) owns the subpath rule. |
| Ports & Adapters | ✅ | Core reads/validates the attribute; the TS adapter owns the path mapping. |
| DX | ✅ | Opt-in; default output byte-identical. |

## Design

| Layer | Change |
|---|---|
| `Metano` | `EmitPackageAttribute.SubpathRoot` (init, default `""`). |
| `Metano.Compiler` | `PackageSubpathRoot` (readonly record struct, `TryFrom`, `None == default`); `EmitPackageInfo.SubpathRoot` / `InvalidSubpathRoot` / `Location` read once by `SymbolHelper.GetEmitPackageInfo`; frontend raises `MS0029` for the TypeScript producer only; `IrCompilation.PackageSubpathRoot`; `IrTypeOrigin.PackageSubpathRoot`; `MS0029`/`MS0030` codes. |
| `Metano.Compiler.TypeScript` | `PathNaming.ToKebabPath` (single namespace→path rule); `PackageExportSubpaths` (`Strip`, `IsUnder`, `Resolve` with `ClaimExportKey`); `TypeTransformer.ReportExportKeyCollisions` raises `MS0030` before caches are written; per-group cache fingerprint includes referenced packages' name + root; `PackageJsonWriter` keys; `IrToTsTypeMapper.BuildTsOrigin` and `TypeTransformer.VisitCrossAssemblyType` apply the producer's root; `TypeScriptTarget.LastPackageSubpathRoot` → `Commands` → writer. |

## Project Structure

```text
specs/008-exports-subpath-root/
├── spec.md
├── plan.md
├── research.md
└── contracts/exports-subpath-root.md
```

## Tests

- `PackageJsonWriter`: stripped / root / outside / segment-boundary / unchanged imports /
  collision winner (no writer diagnostics) / no-root regression.
- Cross-package (`TranspileWithLibrary`): stripped subpath, bare package, outside root, merged
  root + child imports, enum, `[NoContainer]` function, JSX component, invalid root ignored on
  the consumer side.
- Frontend: `MS0029` for a malformed root; none for the Dart target.
- Transform: `MS0030` for sibling-namespace and `--namespace-barrels` collisions; no group cache
  written on collision; per-group cache reused/invalidated by the referenced root.
- `PackageSubpathRoot`: `default == None`, `TryFrom` accepts/rejects.
