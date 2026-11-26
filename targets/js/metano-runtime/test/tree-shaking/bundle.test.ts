/**
 * The size budgets of spec 011 NFR-008 (FR-043): each entry of
 * `size-budgets.json` is bundled, minified, by bun and esbuild; the larger
 * gzipped size must stay within its budget, and the bundles must (not) contain
 * the listed strings.
 */
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { copyFileSync, readFileSync } from "node:fs";
import { join } from "node:path";

import {
  BUNDLERS,
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

test.each([...BUNDLERS])(
  "a kept IDisposable implementer installs Symbol.dispose ahead of its class (%s) (FR-111)",
  async (bundler) => {
    const source = readFileSync(join(import.meta.dir, "entries/dispose-alias.js"), "utf8");
    const code = await bundle(bundler, directory, source);
    const install = code.indexOf('Symbol.for("Symbol.dispose")');

    expect(install).toBeGreaterThanOrEqual(0);
    expect(install).toBeLessThan(code.indexOf("[Symbol.dispose]"));
  },
  BUNDLE_TIMEOUT_MS,
);

test.each([...BUNDLERS])(
  "implementers in an import cycle still install Symbol.dispose ahead of both classes (%s) (FR-111)",
  async (bundler) => {
    for (const fixture of ["dispose-cycle-a.js", "dispose-cycle-b.js"]) {
      copyFileSync(join(import.meta.dir, "fixtures", fixture), join(directory.root, fixture));
    }

    const code = await bundle(
      bundler,
      directory,
      'import { First } from "./dispose-cycle-a.js";\nexport const release = () => new First().partner()[Symbol.dispose]();\n',
    );

    const install = code.indexOf('Symbol.for("Symbol.dispose")');
    const keys = [...code.matchAll(/\[Symbol\.dispose\]\(\)\{/g)].map((match) => match.index);

    expect(install).toBeGreaterThanOrEqual(0);
    expect(keys.length).toBe(2);
    expect(keys.every((key) => install < key)).toBe(true);
  },
  BUNDLE_TIMEOUT_MS,
);

test("size-budgets.json lists every runtime module", () => {
  expect(sizeBudgets.modules).toEqual(listModules(DIST_DIR).map(moduleName));
});

test.each(SIZE_BUNDLERS)(
  "castTo and getRuntimeType bundles import no third-party package (%s) (W5-9 R6)",
  async (bundler) => {
    const castToEntry = [
      'import { castTo } from "./system/reflection/cast-to.js";',
      'import { Int32Type } from "./system/reflection/primitive-types.js";',
      "export function cast(value) { return castTo(value, Int32Type); }",
    ].join("\n");

    const getRuntimeTypeEntry = readFileSync(
      join(import.meta.dir, "entries/runtime-type-of-class.js"),
      "utf8",
    );

    for (const source of [castToEntry, getRuntimeTypeEntry]) {
      const code = await bundle(bundler, directory, source);

      expect(code).not.toContain("decimal.js");
      expect(code).not.toContain("@js-temporal/polyfill");
    }
  },
  BUNDLE_TIMEOUT_MS,
);
