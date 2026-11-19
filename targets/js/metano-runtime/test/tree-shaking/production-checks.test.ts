/**
 * Spec 011 FR-058's acceptance (FR-043, W4-33): a minified bundle that uses `List.get` /
 * `set` and a lowered `foreach` (the generated shape, with its inline `$c` guard):
 *
 * - from `dist-unchecked/` (the `metano-unchecked` condition) with `__METANO_CHECKS__`
 *   defined `false`, under bun, esbuild, Rollup (+ @rollup/plugin-replace) and Vite,
 *   reads and stores `_version` nowhere (the constructor's `defineProperty` keeps the
 *   field's name only as a string), names no flag, and an out-of-range `get` no longer
 *   throws (`removeAt` keeps its check and the out-of-range helper, FR-058);
 * - from `dist/` with nothing defined, keeps both checks.
 */
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { nodeResolve } from "@rollup/plugin-node-resolve";
import replace from "@rollup/plugin-replace";
import terser from "@rollup/plugin-terser";
import { rollup } from "rollup";
import { build as viteBuild } from "vite";
import {
  type BundleDirectory,
  bundle,
  createBundleDirectory,
  THIRD_PARTY,
  UNCHECKED_DIST_DIR,
} from "./harness.ts";

const BUNDLE_TIMEOUT_MS = 60_000;

const ENTRY = readFileSync(join(import.meta.dir, "entries/list-checks.js"), "utf8");

const CHECKS_OFF = { __METANO_CHECKS__: "false" } as const;

let checked: BundleDirectory;
let unchecked: BundleDirectory;

beforeAll(() => {
  checked = createBundleDirectory();
  unchecked = createBundleDirectory(new Set(), UNCHECKED_DIST_DIR);
});

afterAll(() => {
  checked.dispose();
  unchecked.dispose();
});

/** Runs the bundle's exported `run()` (its only third-party imports are external and unused). */
async function runBundle(code: string): Promise<unknown> {
  const url = `data:text/javascript;base64,${Buffer.from(code).toString("base64")}`;
  const module = (await import(url)) as { run(): unknown };

  return module.run();
}

type Bundler = "bun" | "esbuild" | "rollup" | "vite";

let entryCounter = 0;

/** The entry bundled from `directory` by `bundler`, minified, with `define` applied. */
async function bundleEntry(
  bundler: Bundler,
  directory: BundleDirectory,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  if (bundler === "bun" || bundler === "esbuild") {
    return bundle(bundler, directory, ENTRY, define);
  }

  const entry = join(directory.root, `__checks_entry_${entryCounter++}.js`);

  writeFileSync(entry, ENTRY);

  return bundler === "rollup" ? bundleWithRollup(entry, define) : bundleWithVite(directory, entry, define);
}

async function bundleWithRollup(
  entry: string,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  const build = await rollup({
    input: entry,
    external: [...THIRD_PARTY],
    plugins: [replace({ preventAssignment: true, values: { ...define } }), nodeResolve(), terser()],
    onwarn: () => {},
  });

  try {
    const { output } = await build.generate({ format: "esm" });

    return output[0].code;
  } finally {
    await build.close();
  }
}

async function bundleWithVite(
  directory: BundleDirectory,
  entry: string,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  const result = await viteBuild({
    configFile: false,
    logLevel: "silent",
    root: directory.root,
    define: { ...define },
    build: {
      write: false,
      minify: "esbuild",
      lib: { entry, formats: ["es"], fileName: "out" },
      rollupOptions: { external: [...THIRD_PARTY] },
    },
  });
  const output = Array.isArray(result) ? result[0] : result;

  return (output as { output: { code?: string }[] }).output[0]?.code ?? "";
}

describe.each(["bun", "esbuild", "rollup", "vite"] as const)("FR-058 production checks (%s)", (bundler) => {
  test(
    "dist-unchecked with the define: no _version read or store, get does not check its bounds",
    async () => {
      const code = await bundleEntry(bundler, unchecked, CHECKS_OFF);

      expect(code).not.toContain("._version");
      expect(code).not.toContain("__METANO_CHECKS__");
      expect(await runBundle(code)).toEqual([16, undefined]);
    },
    BUNDLE_TIMEOUT_MS,
  );

  test(
    "dist without a define: the version tracking and the bounds check run",
    async () => {
      const code = await bundleEntry(bundler, checked);

      expect(code).toContain("._version");
      expect(await runBundle(code)).toEqual([16, "ArgumentOutOfRangeException"]);
    },
    BUNDLE_TIMEOUT_MS,
  );
});
