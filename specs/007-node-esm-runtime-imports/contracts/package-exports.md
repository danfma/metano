# Contract: metano-runtime public subpaths

Consumers (generated code, hand-written code, tooling) may rely on exactly these resolutions under
Node ESM, Bun, bundlers, and TypeScript `nodenext`/`bundler` resolution.

| Import specifier | Resolves to | Stability |
|---|---|---|
| `metano-runtime` | `dist/index.js` (+ `index.d.ts`) | Public API |
| `metano-runtime/system/json` | `dist/system/json/index.js` | Public API |
| `metano-runtime/system/linq` | `dist/system/linq/index.js` | Public API |
| `metano-runtime/package.json` | `package.json` | Metadata |
| `metano-runtime/<path>.js` | `dist/<path>.js` | Deep import — works, not semver-guaranteed |
| `metano-runtime/<path>` (file) | `dist/<path>.js` | Deep import — works, not semver-guaranteed |
| `metano-runtime/<dir>` (no explicit entry) | — | MUST fail as module-not-found, never as directory import |

Generated code emitted by `Metano.Compiler.TypeScript` only uses the first three rows
(`ImportCollector` emits `metano-runtime` and `metano-runtime/system/linq`; JSON contexts use
`metano-runtime/system/json`).

ESM only: no `require` condition is provided.
