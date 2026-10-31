# Quickstart: Make metano-runtime importable from Node ESM

## Reproduce (before the fix)

```bash
bun install
```

```bash
cd targets/js/metano-runtime && bun run build && node --input-type=module -e 'await import("./dist/index.js")'
```

Expected failure: `ERR_UNSUPPORTED_DIR_IMPORT … dist/system`.

## Verify (after the fix)

```bash
bash scripts/smoke-test-runtime-package.sh
```

Expected: five `ok` import lines, `ok   metano-runtime/system/collections rejected (ERR_MODULE_NOT_FOUND)`, `ok   no extensionless relative specifiers`, `ok   tsgo (…)`, `ok   tsc (…)`, `metano-runtime packaging smoke test passed`, exit 0.

## Regression proof (SC-003)

Temporarily revert `targets/js/metano-runtime/src/index.ts` to `export * from "./system";` and rerun
the script: it must fail at the Node import check and the specifier scan.

## Existing suites (FR-009)

```bash
cd targets/js/metano-runtime && bun test
```

```bash
cd targets/js/sample-todo && bun run build && bun test
```

```bash
cd targets/js/sample-todo-service && bun run build && bun test
```

```bash
cd targets/js/sample-issue-tracker && bun run build && bun test
```

```bash
bunx biome check .
```
