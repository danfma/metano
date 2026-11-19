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
  bundledModules,
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
  /**
   * A marginal row: `maxGzip` bounds this entry's size minus `baselineEntry`'s,
   * both bundled by the same bundler (the cost a class adds to another).
   */
  readonly baselineEntry?: string;
  readonly mustContain?: readonly string[];
  readonly mustNotContain?: readonly string[];
}

const SIZE_BUNDLERS = ["bun", "esbuild"] as const;

interface SizeBudgets {
  readonly budgets: readonly Budget[];
  readonly modules: readonly string[];
}

/** Bundling every entry with two bundlers takes a few seconds on a slow runner. */
const BUNDLE_TIMEOUT_MS = 60_000;

const sizeBudgets = JSON.parse(
  readFileSync(join(import.meta.dir, "size-budgets.json"), "utf8"),
) as SizeBudgets;

/** Each budget under each size bundler, so a failure names both. */
const cases = sizeBudgets.budgets.flatMap((budget) =>
  SIZE_BUNDLERS.map((bundler) => ({ ...budget, bundler })),
);

let directory: BundleDirectory;

beforeAll(() => {
  directory = createBundleDirectory();
});

afterAll(() => {
  directory.dispose();
});

/** The gzipped size of a marginal row's baseline entry; `0` for an ordinary row. */
async function baselineSize(
  bundler: (typeof SIZE_BUNDLERS)[number],
  entry: string | undefined,
): Promise<number> {
  if (entry === undefined) {
    return 0;
  }

  const source = readFileSync(join(import.meta.dir, entry), "utf8");

  return gzipSize(await bundle(bundler, directory, source));
}

describe("size budgets", () => {
  test.each(cases)(
    "$name ($bundler)",
    async (budget) => {
      const source = readFileSync(join(import.meta.dir, budget.entry), "utf8");
      const code = await bundle(budget.bundler, directory, source);
      const baseline = await baselineSize(budget.bundler, budget.baselineEntry);

      if (budget.maxGzip !== undefined) {
        expect(gzipSize(code) - baseline).toBeLessThanOrEqual(budget.maxGzip);
      }

      for (const text of budget.mustContain ?? []) {
        expect(code).toContain(text);
      }

      for (const text of budget.mustNotContain ?? []) {
        expect(code).not.toContain(text);
      }
    },
    BUNDLE_TIMEOUT_MS,
  );
});

test(
  "value equality without a boxing conversion bundles no box module (FR-051)",
  async () => {
    const source = readFileSync(join(import.meta.dir, "entries/value-equality.js"), "utf8");
    const modules = await bundledModules(directory, source);

    expect(modules).toContain("system/equality.js");
    expect(modules.filter((module) => module.startsWith("system/reflection/"))).toEqual([]);
  },
  BUNDLE_TIMEOUT_MS,
);

test(
  "List alone pulls no system/reflection module, getRuntimeType no collections module (D30)",
  async () => {
    const list = await bundledModules(
      directory,
      readFileSync(join(import.meta.dir, "entries/list-only.js"), "utf8"),
    );

    const reflection = await bundledModules(
      directory,
      readFileSync(join(import.meta.dir, "entries/runtime-type-of-class.js"), "utf8"),
    );

    expect(list.filter((module) => module.startsWith("system/reflection/"))).toEqual([]);
    expect(reflection.filter((module) => module.startsWith("system/collections/"))).toEqual([]);
  },
  BUNDLE_TIMEOUT_MS,
);

test("size-budgets.json lists every runtime module", () => {
  expect(sizeBudgets.modules).toEqual(listModules(DIST_DIR).map(moduleName));
});
