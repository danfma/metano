import { expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { join } from "node:path";

const RUNTIME_DIR = join(import.meta.dir, "..");

// Signatures are types only, so tsgo checks them. `tsconfig.typecheck.json` covers every
// `typecheck/` folder under `test/`: the null-root signatures of the JSON serializer
// (FR-034 / FR-037) and the `OrDefault` LINQ terminals (#601).
test("tsgo accepts every typecheck fixture", () => {
  const run = spawnSync(process.execPath, ["x", "tsgo", "-p", "tsconfig.typecheck.json"], {
    cwd: RUNTIME_DIR,
    encoding: "utf8",
  });

  expect(run.stdout + run.stderr).toBe("");
  expect(run.status).toBe(0);
}, 120_000);
