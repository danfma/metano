# Contract: Import Resolution

**Normative for**: FR-012, FR-013, FR-015, FR-016; D2, D3.

## Internal references (generated → generated)

Always resolve to the **defining type's file**, never a barrel (FR-016):

| Case | Specifier |
|---|---|
| Same namespace | `./<kebab-type>` (relative file) |
| Different namespace | `#<alias>/<full-namespace-path>/<kebab-type>` (alias path to the file) |

Guarantee: no generated-to-generated edge passes through an `index` barrel → no ESM import cycles, and internal correctness is independent of barrel generation/pruning.

## External references (consumer → generated)

| Path the consumer writes | Resolves to | Available when |
|---|---|---|
| `<pkg-or-alias>/<full-namespace-path>` | that namespace's **leaf barrel** (all its types) | always (leaf barrels are always generated) |
| `<pkg-or-alias>/<full-namespace-path>/<kebab-type>` | the type's file directly | always |
| `<pkg-or-alias>` (package/alias root) | the **root aggregation barrel** | ONLY when `NamespaceBarrels` opt-in is enabled |

- The leaf-barrel path exists in clean generation and never depends on an orphan (FR-012).
- A bare root import (e.g. `../contracts`) is NOT part of the contract unless the opt-in root barrel is enabled (FR-012). This is the precise resolution of the reported symptom: with full-namespace layout + pruning, the consumer imports `…/vigiata/contracts/profiles` (or enables the root barrel) instead of relying on a stale `../contracts`.

## package.json / alias subpaths

- `imports`: `"#<alias>/*" → "<output-root>/*"` (unchanged).
- `exports`: one entry per emitted **leaf barrel**, at its full-namespace subpath, e.g.
  `"./apis/web-server/contracts/vigiata/contracts/profiles"` → that barrel's `types`/`import`.
- Entries are derived from the actually-emitted barrels, so they always mirror the real layout (FR-013).

## Tree-shaking note

The leaf-barrel + direct-file model is tree-shakable. The root aggregation barrel (`export namespace` tree) is NOT tree-shakable and is therefore opt-in only (FR-015, D2).
