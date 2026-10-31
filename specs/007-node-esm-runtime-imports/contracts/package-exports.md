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

**Intentional tightening (first release after 2.6.1).** Before, `./*` mapped straight onto `dist/*`,
so bundlers and Bun could resolve a *directory* deep import such as `metano-runtime/system/collections`
through its `index.js`. That now resolves to a non-existent `dist/system/collections.js` and fails
everywhere. Generated code never emits such paths; hand-written code should import the root (which
re-exports everything) or name the file, e.g. `metano-runtime/system/collections/index.js`.

**Supported type resolution.** `nodenext`, `node16` and `bundler`. Legacy `node10` cannot resolve the
`system/json` / `system/linq` subpaths (no `typesVersions`); that predates this change.

**Declaration specifiers.** Packed `.d.ts` files keep the source's explicit `.ts` relative specifiers
(tsgo's `rewriteRelativeImportExtensions` only rewrites JS emit). tsc ≥ 5.4 and tsgo resolve them
under all three supported modes. The smoke test rejects any *extensionless* relative specifier in
`.js` or `.d.ts`; tools that cannot read `.ts` specifiers in declarations (some d.ts bundlers) are
not supported.
