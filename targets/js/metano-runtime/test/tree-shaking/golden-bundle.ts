/**
 * The golden bundle check's tooling (spec 011 FR-043), run by
 * `tests/Metano.Tests/TreeShaking/ExpectedOutputBundleTests.cs`:
 *
 *     bun golden-bundle.ts <goldens directory> <report file>
 *
 * Each `*.ts` golden is compiled with tsgo (`--module esnext --target es2022`,
 * without type-checking: its imports name modules that are not there) and the
 * compiled module is bundled as a side-effect-only import by bun, esbuild and
 * Rollup with every import external, in a temporary directory with its own
 * `package.json`, after the canary. A golden that concatenates several emitted
 * files is split at each top-level `import` that follows code; a part that
 * fails to compile or bundle is reported, never skipped.
 *
 * Writes one JSON object to the report file: `{ canary, goldens: { [file]: { [bundler]: code } } }`,
 * where `code` is the module code the bundle keeps ("" when none), or
 * `{ error }` for a golden that could not be checked.
 */
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join } from "node:path";
import terser from "@rollup/plugin-terser";
import * as esbuild from "esbuild";
import { rollup } from "rollup";
import {
  BUNDLERS,
  type Bundler,
  CANARY,
  moduleLoadCode,
  packageBinary,
  REPOSITORY_ROOT,
} from "./harness.ts";

type GoldenResult = Partial<Record<Bundler, string>> | { error: string };

const ENTRY = "__entry.js";

/** A line that only a module's header holds: an import or a re-export. */
const MODULE_HEADER = /^(?:import[\s{*"']|export\s+(?:\*|\{[^}]*\})\s+from\s)/;

const [goldensDirectory, reportFile] = process.argv.slice(2);

if (goldensDirectory === undefined || reportFile === undefined) {
  throw new Error("usage: bun golden-bundle.ts <goldens directory> <report file>");
}

const work = realpathSync(mkdtempSync(join(tmpdir(), "metano-golden-bundle-")));

try {
  writeFileSync(reportFile, JSON.stringify(await check(goldensDirectory)));
} finally {
  rmSync(work, { recursive: true, force: true });
}

/**
 * Checks every golden as emitted and again after Biome formats it with the
 * repository's settings (a consumer's formatter must not cost the annotations
 * that keep it shakable, spec 011 K10); a biome-formatted result is reported
 * under `<bundler> (biome)`.
 */
async function check(directory: string) {
  const goldens = readdirSync(directory).filter((file) => file.endsWith(".ts")).sort();
  const emitted = await checkVariant(join(work, "emitted"), directory, goldens, false);
  const formatted = await checkVariant(join(work, "biome"), directory, goldens, true);
  const results: Record<string, GoldenResult> = {};

  for (const golden of goldens) {
    results[golden] = mergeVariants(emitted.results[golden], formatted.results[golden]);
  }

  return { canary: emitted.canary, goldens: results };
}

function mergeVariants(emitted: GoldenResult | undefined, formatted: GoldenResult | undefined) {
  if (emitted === undefined || "error" in emitted) {
    return emitted ?? { error: "not checked" };
  }

  if (formatted === undefined || "error" in formatted) {
    return formatted ?? emitted;
  }

  const merged: Record<string, string> = { ...emitted };

  for (const [bundler, code] of Object.entries(formatted)) {
    merged[`${bundler} (biome)`] = code ?? "";
  }

  return merged;
}

async function checkVariant(
  root: string,
  directory: string,
  goldens: string[],
  formatWithBiome: boolean,
) {
  const parts = writeParts(root, directory, goldens);

  if (formatWithBiome) {
    formatSources(root);
  }

  compile(root);

  const out = join(root, "out");
  writeFileSync(join(out, "package.json"), JSON.stringify({ name: "golden-probe", type: "module" }));
  writeFileSync(join(out, CANARY), "register();\n");

  const canary = await bundleAll(out, CANARY);
  const results: Record<string, GoldenResult> = {};

  for (const golden of goldens) {
    results[golden] = await checkGolden(out, parts.get(golden) ?? []);
  }

  return { canary, results };
}

/** Formats `<root>/src` with Biome under the repository's formatter settings. */
function formatSources(root: string): void {
  const settings = JSON.parse(readFileSync(join(REPOSITORY_ROOT, "biome.json"), "utf8"));
  const config = { formatter: settings.formatter, javascript: settings.javascript };

  writeFileSync(join(root, "biome.json"), JSON.stringify(config));

  const run = spawnSync(packageBinary("biome"), ["format", "--write", "src"], {
    cwd: root,
    encoding: "utf8",
  });

  if (run.status !== 0) {
    throw new Error(`biome failed (${run.status}): ${run.stdout}${run.stderr}`);
  }
}

/** Writes each golden's parts as `src/<golden>__<n>.ts`; returns the part names per golden. */
function writeParts(root: string, directory: string, goldens: string[]): Map<string, string[]> {
  const src = join(root, "src");
  const parts = new Map<string, string[]>();

  mkdirSync(src, { recursive: true });

  for (const golden of goldens) {
    const names = splitConcatenatedFiles(readFileSync(join(directory, golden), "utf8")).map(
      (text, index) => {
        const name = `${basename(golden, ".ts")}__${index}`;
        writeFileSync(join(src, `${name}.ts`), text);
        return name;
      },
    );

    parts.set(golden, names);
  }

  return parts;
}

/**
 * The emitted files a golden concatenates: a new file starts at a top-level
 * module header (an `import`, or a barrel's `export … from`) that follows
 * code, since a file's imports come first.
 */
function splitConcatenatedFiles(text: string): string[] {
  const parts: string[][] = [[]];
  let seenCode = false;

  for (const line of text.split("\n")) {
    const isHeader = MODULE_HEADER.test(line);

    if (isHeader && seenCode) {
      parts.push([]);
      seenCode = false;
    }

    parts.at(-1)?.push(line);

    if (!isHeader && line.trim().length > 0) {
      seenCode = true;
    }
  }

  return parts.map((lines) => lines.join("\n"));
}

function compile(root: string): void {
  writeFileSync(
    join(root, "tsconfig.json"),
    JSON.stringify({
      compilerOptions: {
        module: "esnext",
        target: "es2022",
        moduleResolution: "bundler",
        noCheck: true,
        noEmitOnError: false,
        skipLibCheck: true,
        allowImportingTsExtensions: true,
        rewriteRelativeImportExtensions: true,
        experimentalDecorators: false,
        outDir: "out",
        rootDir: "src",
      },
      include: ["src"],
    }),
  );

  const run = spawnSync(packageBinary("tsgo"), ["-p", root], { encoding: "utf8" });

  if (run.status !== 0 && run.status !== 2) {
    throw new Error(`tsgo failed (${run.status}): ${run.stdout}${run.stderr}`);
  }
}

async function checkGolden(out: string, parts: string[]): Promise<GoldenResult> {
  const result: Partial<Record<Bundler, string>> = {};

  try {
    for (const part of parts) {
      const codes = await bundleAll(out, `${part}.js`);

      for (const bundler of BUNDLERS) {
        result[bundler] = [result[bundler], codes[bundler]].filter(Boolean).join("\n");
      }
    }

    return result;
  } catch (error) {
    return { error: error instanceof Error ? error.message : String(error) };
  }
}

/** The module code a side-effect-only import of `module` keeps under each bundler. */
async function bundleAll(out: string, module: string): Promise<Record<Bundler, string>> {
  const entry = join(out, ENTRY);
  const target = join(out, module);

  writeFileSync(entry, `import "./${module}";\n`);

  const isExternal = (specifier: string, importer: string | undefined) =>
    (importer !== undefined && importer !== entry && importer.length > 0) ||
    !(specifier === `./${module}` || specifier === target);

  return {
    bun: moduleLoadCode(await withBun(entry, isExternal)),
    esbuild: moduleLoadCode(await withEsbuild(entry, isExternal)),
    rollup: moduleLoadCode(await withRollup(entry, target)),
  };
}

type IsExternal = (specifier: string, importer: string | undefined) => boolean;

async function withBun(entry: string, isExternal: IsExternal): Promise<string> {
  const result = await Bun.build({
    entrypoints: [entry],
    minify: true,
    target: "browser",
    format: "esm",
    throw: false,
    plugins: [
      {
        name: "everything-external",
        setup(build) {
          build.onResolve({ filter: /.*/ }, (args) =>
            args.kind !== "entry-point-build" && isExternal(args.path, args.importer || undefined)
              ? { path: args.path, external: true }
              : undefined,
          );
        },
      },
    ],
  });

  if (!result.success) {
    throw new Error(`bun: ${result.logs.map(String).join("; ")}`);
  }

  return (await result.outputs[0]?.text()) ?? "";
}

async function withEsbuild(entry: string, isExternal: IsExternal): Promise<string> {
  const result = await esbuild.build({
    entryPoints: [entry],
    bundle: true,
    minify: true,
    format: "esm",
    write: false,
    logLevel: "silent",
    plugins: [
      {
        name: "everything-external",
        setup(build) {
          build.onResolve({ filter: /.*/ }, (args) =>
            args.kind !== "entry-point" && isExternal(args.path, args.importer || undefined)
              ? { path: args.path, external: true }
              : undefined,
          );
        },
      },
    ],
  });

  return result.outputFiles[0]?.text ?? "";
}

async function withRollup(entry: string, target: string): Promise<string> {
  const build = await rollup({
    input: entry,
    external: (specifier, importer) =>
      importer !== undefined && specifier !== target && !(importer === entry && specifier.startsWith("./")),
    plugins: [
      {
        name: "resolve-the-part",
        resolveId: (specifier, importer) =>
          importer === entry && specifier.startsWith("./") ? target : null,
      },
      terser(),
    ],
    onwarn: () => {},
  });

  try {
    const { output } = await build.generate({ format: "esm" });

    return output[0].code;
  } finally {
    await build.close();
  }
}
