/**
 * The side-effect-only import suite (spec 011 FR-043): `import "./<module>.js"`
 * of every runtime module bundles to no module code under bun, esbuild and
 * Rollup, except for the modules `allowlist.json` names. The canary runs first
 * under the same configuration and must be kept, so a configuration that
 * trusted `sideEffects: false` would fail here before passing a real module.
 */
import { afterAll, beforeAll, describe, expect, setDefaultTimeout, test } from "bun:test";
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, relative } from "node:path";
import { nodeResolve } from "@rollup/plugin-node-resolve";
import terser from "@rollup/plugin-terser";
import { rollup } from "rollup";
import {
  BUNDLERS,
  type BundleDirectory,
  bundle,
  CANARY,
  createBundleDirectory,
  DIST_DIR,
  forceModuleSideEffects,
  listModules,
  moduleLoadCode,
  moduleName,
  RUNTIME_DIR,
  THIRD_PARTY,
} from "./harness.ts";

/**
 * The most the allowlist may ever hold: the measured state when the suite
 * landed. Growing the list means growing this set too, which a review sees.
 */
const MAXIMAL_ALLOWLIST: ReadonlySet<string> = new Set<string>([]);

/** The package subpaths a consumer imports, resolved through `node_modules`. */
const SUBPATH_EXPORTS = [
  "metano-runtime",
  "metano-runtime/system/json",
  "metano-runtime/system/linq",
  "metano-runtime/system/collections",
  "metano-runtime/system/exceptions",
  "metano-runtime/system/reflection",
];

/** Each test bundles with up to three bundlers; a slow runner needs more than the default 5 s. */
setDefaultTimeout(60_000);

const allowlist: string[] = JSON.parse(
  readFileSync(join(import.meta.dir, "allowlist.json"), "utf8"),
).modules;

const allowed = new Set(allowlist);
const modules = listModules(DIST_DIR);
const checkedModules = modules.filter((module) => !allowed.has(moduleName(module)));

let strict: BundleDirectory;
let tracked: BundleDirectory;

beforeAll(() => {
  strict = createBundleDirectory();
  tracked = createBundleDirectory(allowed);
});

afterAll(() => {
  strict.dispose();
  tracked.dispose();
});

/** The module code a side-effect-only import of `module` keeps under `bundler`. */
async function loadCode(
  bundler: (typeof BUNDLERS)[number],
  directory: BundleDirectory,
  module: string,
): Promise<string> {
  return moduleLoadCode(await bundle(bundler, directory, `import "./${module}";\n`));
}

describe("the canary", () => {
  test.each([...BUNDLERS])("is kept by %s", async (bundler) => {
    expect(await loadCode(bundler, strict, CANARY)).toContain("register");
    expect(await loadCode(bundler, tracked, CANARY)).toContain("register");
  });

  test("is kept by Rollup through node_modules with sideEffects: false", async () => {
    const code = await bundleThroughNodeModules("metano-canary");

    expect(code).toContain("register");
  });
});

describe("the allowlist", () => {
  test("is sorted, unique and names existing modules", () => {
    expect(allowlist).toEqual([...new Set(allowlist)].sort());
    expect(allowlist.filter((name) => !modules.includes(`${name}.js`))).toEqual([]);
  });

  test("never grows beyond the maximal set", () => {
    expect(allowlist.filter((name) => !MAXIMAL_ALLOWLIST.has(name))).toEqual([]);
  });

  test.each(allowlist)("still names a module with load-time code: %s", async (name) => {
    const others = new Set(allowlist.filter((other) => other !== name));
    const directory = createBundleDirectory(others);

    try {
      const codes = await Promise.all(
        BUNDLERS.map((bundler) => loadCode(bundler, directory, `${name}.js`)),
      );

      expect(codes.some((code) => code.length > 0)).toBe(true);
    } finally {
      directory.dispose();
    }
  });
});

describe("a side-effect-only import keeps no module code", () => {
  test.each(checkedModules)("%s", async (module) => {
    for (const bundler of BUNDLERS) {
      expect({ bundler, code: await loadCode(bundler, tracked, module) }).toEqual({
        bundler,
        code: "",
      });
    }
  });

  test("the root index keeps no code beyond the allowlisted modules'", async () => {
    for (const bundler of BUNDLERS) {
      expect(await loadCode(bundler, tracked, "index.js")).toBe("");
    }
  });

  test.each(SUBPATH_EXPORTS)("%s, resolved through node_modules by Rollup", async (specifier) => {
    expect(await bundleThroughNodeModules(specifier)).toBe("");
  });
});

/**
 * `import "<specifier>"` bundled by Rollup from a consumer directory whose
 * `node_modules` links the real runtime package (with its `sideEffects: false`)
 * and a canary package declaring the same, with every module's side effects
 * forced back on except the allowlisted runtime modules.
 */
async function bundleThroughNodeModules(specifier: string): Promise<string> {
  const consumer = realpathSync(mkdtempSync(join(tmpdir(), "metano-tree-shaking-consumer-")));

  try {
    linkPackages(consumer);

    const entry = join(consumer, "entry.js");
    writeFileSync(entry, `import "${specifier}";\n`);

    const build = await rollup({
      input: entry,
      external: [...THIRD_PARTY],
      plugins: [forceModuleSideEffects(isAllowlistedRuntimeModule), nodeResolve(), terser()],
      onwarn: () => {},
    });

    try {
      const { output } = await build.generate({ format: "esm" });

      return moduleLoadCode(output[0].code);
    } finally {
      await build.close();
    }
  } finally {
    rmSync(consumer, { recursive: true, force: true });
  }
}

function linkPackages(consumer: string): void {
  const nodeModules = join(consumer, "node_modules");
  const canary = join(nodeModules, "metano-canary");

  mkdirSync(canary, { recursive: true });
  symlinkSync(RUNTIME_DIR, join(nodeModules, "metano-runtime"), "dir");
  writeFileSync(join(consumer, "package.json"), JSON.stringify({ name: "consumer", type: "module" }));
  writeFileSync(
    join(canary, "package.json"),
    readFileSync(join(import.meta.dir, "fixtures/canary-package.json"), "utf8"),
  );
  writeFileSync(join(canary, "canary.js"), "register();\n");
}

function isAllowlistedRuntimeModule(id: string): boolean {
  const path = relative(realpathSync(DIST_DIR), id).split("\\").join("/");

  return !path.startsWith("..") && allowed.has(moduleName(path));
}
