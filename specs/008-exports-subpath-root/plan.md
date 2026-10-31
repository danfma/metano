# Implementation Plan: Opt-in exports subpath root

**Branch**: `feat/267-exports-subpath-root` | **Date**: 2026-10-31 | **Spec**: [spec.md](./spec.md)

## Summary

Add `SubpathRoot` to `[EmitPackage]`, carry it through the frontend (`EmitPackageInfo`,
`IrCompilation.PackageSubpathRoot`, `IrTypeOrigin.PackageSubpathRoot`), and apply one
TypeScript-side mapping (`ExportsSubpathRoot`) in two places: the `package.json#exports` key
builder and the cross-package subpath builder (type origins and `[NoContainer]` exports).

## Technical Context

C# 14 / .NET 10, Roslyn 5.3, TUnit. TypeScript target only; Dart ignores the new IR field.

## Constitution Check

| Principle | Status | Notes |
|---|---|---|
| Spec as source of truth | ✅ | This spec amends 005 FR-002/FR-013 before implementation. |
| Clean Code / expressive names | ✅ | One small value type (`ExportsSubpathRoot`) owns the rule. |
| Ports & Adapters | ✅ | Core reads/validates the attribute; the TS adapter owns the path mapping. |
| DX | ✅ | Opt-in; default output byte-identical. |

## Design

| Layer | Change |
|---|---|
| `Metano` | `EmitPackageAttribute.SubpathRoot` (init, default `""`). |
| `Metano.Compiler` | `EmitPackageInfo.SubpathRoot` (validated; invalid → null) + `IsValidSubpathRoot`; frontend raises `MS0029` for the producer; `IrCompilation.PackageSubpathRoot`; `IrTypeOrigin.PackageSubpathRoot`. |
| `Metano.Compiler.TypeScript` | `ExportsSubpathRoot` (kebab root path, `Apply(path)`); `PackageJsonWriter` keys + collision `MS0029`; `IrToTsTypeMapper.BuildTsOrigin` and `TypeTransformer.VisitCrossAssemblyType` apply the producer's root; `TypeScriptTarget.LastPackageSubpathRoot`; `Commands` passes it and exits 1 on Error. |

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
  collision (root aggregation barrel and sibling namespace) / no-root regression.
- Cross-package (`TranspileWithLibrary`): stripped subpath, bare package, outside root,
  `[NoContainer]` function import, invalid root ignored on consumer side.
- Frontend: `MS0029` for a malformed root.
