/**
 * Spec 011 FR-058's acceptance (FR-043): with `__METANO_CHECKS__` defined `false`,
 * a minified bun / esbuild bundle that uses `List.get` / `set` and a lowered `foreach`
 * reads and stores `_version` nowhere (the constructor's `defineProperty` keeps the
 * field's name only as a string), and an out-of-range `get` no longer throws; with
 * nothing defined, both checks are present.
 */
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { type BundleDirectory, bundle, createBundleDirectory } from "./harness.ts";

const BUNDLE_TIMEOUT_MS = 60_000;

const ENTRY = readFileSync(join(import.meta.dir, "entries/list-checks.js"), "utf8");

const CHECKS_OFF = { __METANO_CHECKS__: "false" } as const;

let directory: BundleDirectory;

beforeAll(() => {
  directory = createBundleDirectory();
});

afterAll(() => {
  directory.dispose();
});

/** Runs the bundle's exported `run()` (its only third-party imports are external and unused). */
async function runBundle(code: string): Promise<unknown> {
  const url = `data:text/javascript;base64,${Buffer.from(code).toString("base64")}`;
  const module = (await import(url)) as { run(): unknown };

  return module.run();
}

describe.each(["bun", "esbuild"] as const)("FR-058 production checks (%s)", (bundler) => {
  test(
    "defined false: no _version read or store, get does not check its bounds",
    async () => {
      const code = await bundle(bundler, directory, ENTRY, CHECKS_OFF);

      expect(code).not.toContain("._version");
      expect(code).not.toContain("__METANO_CHECKS__");
      expect(await runBundle(code)).toEqual([16, undefined]);
    },
    BUNDLE_TIMEOUT_MS,
  );

  test(
    "not defined: the version tracking and the bounds check run",
    async () => {
      const code = await bundle(bundler, directory, ENTRY);

      expect(code).toContain("._version");
      expect(await runBundle(code)).toEqual([16, "ArgumentOutOfRangeException"]);
    },
    BUNDLE_TIMEOUT_MS,
  );
});
