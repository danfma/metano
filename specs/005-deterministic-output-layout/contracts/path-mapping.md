# Contract: Type → On-disk Path Mapping

**Normative for**: FR-001, FR-002, FR-003, FR-004; cross-package R2.

## Rule

For a transpilable type with full C# namespace `N` (segments `s₁.s₂.….sₙ`, possibly empty) and emitted name `T`:

```
path(N, T, ext) =
  if N == ""  →  kebab(T) + ext
  else        →  kebab(s₁) + "/" + … + "/" + kebab(sₙ) + "/" + kebab(T) + ext
```

- `ext` is `.tsx` for JSX-emitting types, else `.ts`.
- `kebab` is the existing `SymbolHelper.ToKebabCase`.
- No prefix is stripped. The output directory (reached by npm package name or `#<alias>`) is the only container.
- The result depends solely on `(N, T, ext)` — never on which other types are present.

## Examples

| Full namespace | Type | Path |
|---|---|---|
| `Vigiata.Contracts.Profiles` | `UserProfile` | `vigiata/contracts/profiles/user-profile.ts` |
| `Vigiata.Contracts.Serialization` | `ContractsSerializerContext` | `vigiata/contracts/serialization/contracts-serializer-context.ts` |
| `Vigiata.Contracts` | `Foo` | `vigiata/contracts/foo.ts` |
| `""` (global) | `Bar` | `bar.ts` |

## Stability guarantees

- **Single ↔ multi type**: adding/removing any other type does not change this type's path (FR-001, SC-001/SC-002).
- **No single-type collapse**: a lone type still emits at its full-namespace path (FR-004).
- **Cross-package identical**: a referenced assembly's type maps by the same rule (`ComputeSubPath` with empty assembly root) (R2).

## Out of scope

- The mapping does not introduce per-project configuration. (The opt-in `NamespaceBarrels` affects barrels, not type-file paths.)
