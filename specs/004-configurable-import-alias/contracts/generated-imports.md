# Contract: generated import specifiers & package.json entries

Defines the exact emitted shape, as a function of the alias state. These are the
observable outputs the acceptance scenarios assert against.

## In-file import specifiers

For a target type whose namespace (after the project root namespace is stripped and
kebab-cased) is `<ns>`:

| Case | Unset (default) | Set: alias `contracts` |
|------|-----------------|------------------------|
| Cross-namespace, non-root | `import { T } from "#/<ns>"` | `import { T } from "#contracts/<ns>"` |
| Cross-namespace, root namespace | `import { T } from "#"` | `import { T } from "#contracts"` |
| Same namespace | `import { T } from "./<kebab-type>"` | `import { T } from "./<kebab-type>"` (unchanged) |
| External package (`[EmitPackage("pkg")]`) | `import { T } from "pkg/<sub>"` | `import { T } from "pkg/<sub>"` (unchanged) |

## package.json `imports` entries

| Alias state | Keys emitted |
|-------------|--------------|
| Unset | `#/*` (+ `#` if a root barrel exists) |
| Set `contracts` | `#contracts/*` (+ `#contracts` if a root barrel exists) |

Worked example — output dir `src/abc/contracts` inside a host package whose root is
the parent, alias `contracts`:

```jsonc
{
  "imports": {
    // host project's own alias — PRESERVED, never written by Metano
    "#/*": { "default": "./src/*.ts" },

    // Metano-managed, alias-scoped, depth-correct, no double slash
    "#contracts/*": {
      "types":   "./dist/abc/contracts/*.d.ts",
      "import":  "./dist/abc/contracts/*.js",
      "default": "./src/abc/contracts/*.ts"
    }
  }
}
```

## Invariants

1. **Isolation (FR-003)**: when an alias is set, Metano writes only the
   alias-scoped keys; it never adds or modifies `#`/`#/*`.
2. **Backward compatibility (FR-004)**: when unset, every emitted byte equals the
   pre-feature output.
3. **External imports unaffected (FR-006)**: package-name specifiers are identical
   regardless of alias.
4. **Well-formed paths (FR-008)**: no doubled path separators at any depth.
5. **Cycle detection (FR-009)**: a circular reference is reported (diagnostic
   MS0005) whether or not an alias is set.
