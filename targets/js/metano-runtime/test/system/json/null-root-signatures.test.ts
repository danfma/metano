import { expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { join } from "node:path";

const RUNTIME_DIR = join(import.meta.dir, "..", "..", "..");

// The signatures are types only, so tsgo checks them: `typecheck/null-root-signatures.ts`
// asserts the FR-034 / FR-037 return types and the strict-mode errors of the migration.
// The project covers every `typecheck/` folder, so the run also checks
// `test/system/linq/typecheck/or-default-signatures.ts`.
test("tsgo accepts the null-root signature checks", () => {
  const run = spawnSync(process.execPath, ["x", "tsgo", "-p", "tsconfig.typecheck.json"], {
    cwd: RUNTIME_DIR,
    encoding: "utf8",
  });

  expect(run.stdout + run.stderr).toBe("");
  expect(run.status).toBe(0);
}, 120_000);
