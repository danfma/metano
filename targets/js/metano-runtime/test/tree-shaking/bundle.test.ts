/**
 * The size budgets of spec 011 NFR-008 (FR-043): each entry of
 * `size-budgets.json` is bundled, minified, by bun and esbuild; the larger
 * gzipped size must stay within its budget, and the bundles must (not) contain
 * the listed strings.
 */
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import {
  type BundleDirectory,
  bundle,
  createBundleDirectory,
  DIST_DIR,
  gzipSize,
  listModules,
  moduleName,
} from "./harness.ts";

interface Budget {
  readonly name: string;
  readonly entry: string;
  readonly maxGzip?: number;
  readonly mustContain?: readonly string[];
  readonly mustNotContain?: readonly string[];
}

const SIZE_BUNDLERS = ["bun", "esbuild"] as const;

const budgets = JSON.parse(readFileSync(join(import.meta.dir, "size-budgets.json"), "utf8"));

let directory: BundleDirectory;

beforeAll(() => {
  directory = createBundleDirectory();
});

afterAll(() => {
  directory.dispose();
});

describe("size budgets", () => {
  test.each(budgets.budgets as Budget[])("$name", async (budget) => {
    const source = readFileSync(join(import.meta.dir, budget.entry), "utf8");

    for (const bundler of SIZE_BUNDLERS) {
      const code = await bundle(bundler, directory, source);

      if (budget.maxGzip !== undefined) {
        expect({ bundler, size: Math.min(gzipSize(code), budget.maxGzip) }).toEqual({
          bundler,
          size: gzipSize(code),
        });
      }

      for (const text of budget.mustContain ?? []) {
        expect({ bundler, contains: code.includes(text) }).toEqual({ bundler, contains: true });
      }

      for (const text of budget.mustNotContain ?? []) {
        expect({ bundler, contains: code.includes(text) }).toEqual({ bundler, contains: false });
      }
    }
  });
});

test("size-budgets.json lists every runtime module", () => {
  expect(budgets.modules).toEqual(listModules(DIST_DIR).map(moduleName));
});
