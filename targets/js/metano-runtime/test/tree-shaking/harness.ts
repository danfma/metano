/**
 * The bundling harness of the tree-shaking suites (spec 011 FR-043). It never
 * trusts the runtime's `sideEffects: false`: modules are bundled from a copy in
 * a temporary directory with its own `package.json` (bundlers read the nearest
 * one upward), every third-party package is external, and every run starts
 * with the canary, a module whose only content is a top-level call.
 *
 * The one thing a directory may declare pure is the allowlist: the modules
 * whose load-time code is known and tracked (`allowlist.json`). They are left
 * out of the directory's `sideEffects` list (Rollup: `moduleSideEffects`), so
 * a module that merely imports one is judged on its own code. Every other
 * module, and the canary, is listed as having side effects.
 */
import { randomUUID } from "node:crypto";

import {
  cpSync,
  existsSync,
  mkdtempSync,
  readdirSync,
  realpathSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";

import { tmpdir } from "node:os";
import { join, relative, resolve } from "node:path";
import { nodeResolve } from "@rollup/plugin-node-resolve";
import terser from "@rollup/plugin-terser";
import * as esbuild from "esbuild";
import { rollup, type Plugin as RollupPlugin } from "rollup";

/** The runtime package's directory. */
export const RUNTIME_DIR = resolve(import.meta.dir, "../..");

/** The repository root, whose `node_modules` holds the workspace's hoisted packages. */
export const REPOSITORY_ROOT = resolve(RUNTIME_DIR, "../../..");

/**
 * The path of an installed package binary (`tsgo`): the runtime's own
 * `node_modules/.bin`, or the repository root's when bun hoisted it there.
 */
export function packageBinary(name: string): string {
  const local = join(RUNTIME_DIR, "node_modules", ".bin", name);

  return existsSync(local) ? local : join(REPOSITORY_ROOT, "node_modules", ".bin", name);
}

/** The built modules every suite bundles from. */
export const DIST_DIR = join(RUNTIME_DIR, "dist");

/**
 * The runtime's unchecked dist (spec 011 FR-058): the checks folded off,
 * selected by `metano-unchecked`.
 */
export const UNCHECKED_DIST_DIR = join(RUNTIME_DIR, "dist-unchecked");

/** The runtime's dependencies: never bundled, always external. */
export const THIRD_PARTY = ["@js-temporal/polyfill", "decimal.js"] as const;

/** The bundlers every check runs through. */
export const BUNDLERS = ["bun", "esbuild", "rollup"] as const;

export type Bundler = (typeof BUNDLERS)[number];

/** The canary fixture's file name inside a bundling directory. */
export const CANARY = "canary.js";

/** A temporary copy of a module tree that bundlers can resolve without trusting `sideEffects`. */
export interface BundleDirectory {
  readonly root: string;
  /** The allowlisted modules this directory declares pure (names without `.js`). */
  readonly pure: ReadonlySet<string>;
  dispose(): void;
}

/**
 * Copies `source` (the runtime's `dist` by default) into a fresh temporary
 * directory with its own `package.json`, plus the canary. Without `pure`
 * modules the manifest has no `sideEffects` field; with them, it lists every
 * other module (and the canary) as having side effects.
 */
export function createBundleDirectory(
  pure: ReadonlySet<string> = new Set(),
  source: string = DIST_DIR,
): BundleDirectory {
  // The real path: bundlers report importers resolved through symlinks (macOS /var).
  const root = realpathSync(mkdtempSync(join(tmpdir(), "metano-tree-shaking-")));

  cpSync(source, root, { recursive: true });
  writeFileSync(join(root, CANARY), "register();\n");
  writeFileSync(join(root, "package.json"), JSON.stringify(manifestOf(root, pure)));

  return {
    root,
    pure,
    dispose: () => rmSync(root, { recursive: true, force: true }),
  };
}

function manifestOf(root: string, pure: ReadonlySet<string>): object {
  const manifest = { name: "metano-tree-shaking-probe", private: true, type: "module" };

  if (pure.size === 0) {
    return manifest;
  }

  const withSideEffects = listModules(root)
    .filter((module) => !pure.has(moduleName(module)))
    .map((module) => `./${module}`);

  return { ...manifest, sideEffects: withSideEffects };
}

/** Every `.js` module under `root`, as a `/`-separated path relative to it, sorted. */
export function listModules(root: string): string[] {
  const modules: string[] = [];

  const walk = (directory: string) => {
    for (const name of readdirSync(directory)) {
      const path = join(directory, name);

      if (statSync(path).isDirectory()) {
        walk(path);
      } else if (name.endsWith(".js")) {
        modules.push(relative(root, path).split("\\").join("/"));
      }
    }
  };

  walk(root);

  return modules.sort();
}

/** A module path without its `.js` extension (`system/uuid`), as the allowlists name it. */
export function moduleName(modulePath: string): string {
  return modulePath.replace(/\.js$/, "");
}

/**
 * The code a bundle keeps besides import statements: what runs at module load.
 * External `import` statements (bare or named) and an empty `export {}` are
 * not module code; whitespace and comments are dropped.
 */
export function moduleLoadCode(bundle: string): string {
  return bundle
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/^\s*\/\/.*$/gm, "")
    .replace(/import\s*(?:[^"';]*?from\s*)?["'][^"']+["']\s*;?/g, "")
    .replace(/export\s*\{\s*\}\s*;?/g, "")
    .trim();
}

/**
 * Bundles `source` (an entry module written into the directory) with
 * `bundler`, minified, with every third-party package left as an import.
 */
export async function bundle(
  bundler: Bundler,
  directory: BundleDirectory,
  source: string,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  const entry = join(directory.root, `__entry_${randomUUID()}.js`);

  writeFileSync(entry, source);

  try {
    return await BUNDLE[bundler](entry, isPureIn(directory), define);
  } finally {
    rmSync(entry, { force: true });
  }
}

/**
 * The runtime modules esbuild resolves for `source` (an entry module written into the
 * directory), as `/`-separated paths relative to the directory, sorted: what a bundle
 * pulls in, whatever its minified text looks like.
 */
export async function bundledModules(
  directory: BundleDirectory,
  source: string,
): Promise<string[]> {
  const entry = join(directory.root, `__entry_${randomUUID()}.js`);

  writeFileSync(entry, source);

  try {
    const result = await esbuild.build({
      entryPoints: [entry],
      bundle: true,
      format: "esm",
      write: false,
      metafile: true,
      logLevel: "silent",
      external: [...THIRD_PARTY],
    });

    return Object.keys(result.metafile.inputs)
      .map((input) => relative(directory.root, resolve(input)).split("\\").join("/"))
      .filter((module) => module !== relative(directory.root, entry))
      .sort();
  } finally {
    rmSync(entry, { force: true });
  }
}

/** Gzipped size of `code` in bytes. */
export function gzipSize(code: string): number {
  return Bun.gzipSync(code).byteLength;
}

/** Whether a resolved module id is one the directory declares pure (Rollup reads no manifest). */
type IsPure = (id: string) => boolean;

function isPureIn(directory: BundleDirectory): IsPure {
  return (id) => directory.pure.has(moduleName(relative(directory.root, id).split("\\").join("/")));
}

function isThirdParty(specifier: string): boolean {
  return THIRD_PARTY.some((name) => specifier === name || specifier.startsWith(`${name}/`));
}

type BundleFunction = (
  entry: string,
  isPure: IsPure,
  define?: Readonly<Record<string, string>>,
) => Promise<string>;

const BUNDLE: Record<Bundler, BundleFunction> = {
  bun: bundleWithBun,
  esbuild: bundleWithEsbuild,
  rollup: bundleWithRollup,
};

async function bundleWithBun(
  entry: string,
  _isPure: IsPure,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  const result = await Bun.build({
    entrypoints: [entry],
    minify: true,
    define: { ...define },
    target: "browser",
    format: "esm",
    external: [...THIRD_PARTY],
  });

  if (!result.success) {
    throw new AggregateError(result.logs, `bun failed to bundle ${entry}`);
  }

  return (await result.outputs[0]?.text()) ?? "";
}

async function bundleWithEsbuild(
  entry: string,
  _isPure: IsPure,
  define?: Readonly<Record<string, string>>,
): Promise<string> {
  const result = await esbuild.build({
    entryPoints: [entry],
    bundle: true,
    minify: true,
    define: { ...define },
    format: "esm",
    write: false,
    logLevel: "silent",
    external: [...THIRD_PARTY],
  });

  return result.outputFiles[0]?.text ?? "";
}

async function bundleWithRollup(entry: string, isPure: IsPure): Promise<string> {
  const build = await rollup({
    input: entry,
    external: (specifier) => isThirdParty(specifier),
    plugins: [nodeResolve(), terser()],
    treeshake: { moduleSideEffects: (id) => !isPure(id) },
    onwarn: () => {},
  });

  try {
    const { output } = await build.generate({ format: "esm" });

    return output[0].code;
  } finally {
    await build.close();
  }
}

/**
 * A Rollup plugin that resolves through `node_modules` but keeps every module's
 * side effects except the ones `isPure` names: `@rollup/plugin-node-resolve`
 * honours `sideEffects: false` even under `treeshake.moduleSideEffects: true`,
 * so the flag is overridden per resolved module.
 */
export function forceModuleSideEffects(isPure: (id: string) => boolean): RollupPlugin {
  return {
    name: "metano-force-module-side-effects",
    async resolveId(source, importer, options) {
      const resolved = await this.resolve(source, importer, { ...options, skipSelf: true });

      if (resolved === null || resolved.external) {
        return resolved;
      }

      return { ...resolved, moduleSideEffects: !isPure(resolved.id) };
    },
  };
}
