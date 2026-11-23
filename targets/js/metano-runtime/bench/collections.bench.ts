/**
 * Spec 011's collection benchmark (FR-039, NFR-001; plan.md § Benchmark plan,
 * research R17). Hot-helper shape: each kernel makes one pass over 1e4 elements and
 * is called 500 times per run (5e6 operations); 10 untimed warm-up runs, 21 timed
 * runs, the median. Every (case, checks mode, role) runs in a fresh process of the
 * engine that runs this script, together with the calibration kernel
 * (`a[i] = a[i] + 1` over a native array), and the gating figure is the normalized
 * overhead per operation: (variant − baseline) / operations × C_ref / C_measured.
 *
 * Usage: `bun run bench` (Bun) / `bun run bench:node` (Node.js)
 *        [--case <text>] [--check] [--json <file>]
 */
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { cpus, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import * as esbuild from "esbuild";
import { type BenchCase, CASES } from "./collections/cases.ts";

const N = 10_000;
const CALLS = 500;
const WARMUP_RUNS = 10;
const TIMED_RUNS = 21;

/** Research R17's reference machine: Apple M1 Pro, Node.js 24.11, Bun 1.4.2. */
const C_REF_NS = { node: 0.59, bun: 0.47 } as const;

type Engine = keyof typeof C_REF_NS;
type Mode = "checked" | "unchecked";
type Role = "baseline" | "variant";

const MODE_LABEL: Record<Mode, string> = {
  checked: "dist/ (checked, no define)",
  unchecked: "dist-unchecked/ + define false",
};

const RUNTIME_DIR = resolve(fileURLToPath(new URL(".", import.meta.url)), "..");
/** The collections of each published dist (spec 011 FR-058, W4-33). */
const DIST: Record<Mode, string> = {
  checked: join(RUNTIME_DIR, "dist", "system", "collections"),
  unchecked: join(RUNTIME_DIR, "dist-unchecked", "system", "collections"),
};

/** The vendored 2.x `HashSet` (bench-only fixture) that the 2.x comparison row runs. */
const LEGACY_HASH_SET = join(RUNTIME_DIR, "bench", "collections", "legacy-hash-set.ts");

const engine: Engine = typeof Bun === "undefined" ? "node" : "bun";
const options = parseOptions(process.argv.slice(2));

interface Measurement {
  readonly calibrationMs: number;
  readonly medianMs: number;
}

interface Row {
  readonly name: string;
  readonly mode: Mode;
  readonly budgetNs: number | undefined;
  readonly deferredTo: string | undefined;
  readonly baselineMs: number;
  readonly variantMs: number;
  readonly rawNs: number;
  readonly cMeasuredNs: number;
  readonly normalizedNs: number;
}

const directory = mkdtempSync(join(tmpdir(), "metano-collections-bench-"));

async function runAll(): Promise<Row[]> {
  const rows: Row[] = [];

  for (const benchCase of CASES.filter((item) => item.name.includes(options.filter))) {
    const modes: Mode[] = benchCase.everyMode === true ? ["checked", "unchecked"] : ["checked"];

    for (const mode of modes) {
      const file = await buildModule(benchCase, mode);
      const baseline = measure(file, "baseline");
      const variant = measure(file, "variant");

      rows.push(rowOf(benchCase, mode, baseline, variant));
    }
  }

  return rows;
}

function rowOf(benchCase: BenchCase, mode: Mode, baseline: Measurement, variant: Measurement): Row {
  const ops = CALLS * opsOf(benchCase);
  const rawNs = ((variant.medianMs - baseline.medianMs) * 1e6) / ops;
  const cMeasuredNs = (((baseline.calibrationMs + variant.calibrationMs) / 2) * 1e6) / (CALLS * N);

  return {
    name: benchCase.name,
    mode,
    budgetNs: budgetFor(benchCase),
    deferredTo: benchCase.deferredTo,
    baselineMs: baseline.medianMs,
    variantMs: variant.medianMs,
    rawNs,
    cMeasuredNs,
    normalizedNs: (rawNs * C_REF_NS[engine]) / cMeasuredNs,
  };
}

/** The case's NFR-001 budget on this engine. */
function budgetFor(benchCase: BenchCase): number | undefined {
  const budget = benchCase.budgetNs;

  return typeof budget === "object" ? budget[engine] : budget;
}

function opsOf(benchCase: BenchCase): number {
  return benchCase.ops === "N" ? N : 2 * N;
}

let moduleCounter = 0;

/** One self-running ES module per case and mode, bundled against the mode's dist. */
async function buildModule(benchCase: BenchCase, mode: Mode): Promise<string> {
  const entry = join(directory, `case-${moduleCounter}.mjs`);
  const output = join(directory, `case-${moduleCounter++}.bundle.mjs`);

  writeFileSync(entry, moduleSource(benchCase, mode));

  await esbuild.build({
    entryPoints: [entry],
    outfile: output,
    bundle: true,
    format: "esm",
    platform: "neutral",
    logLevel: "silent",
    external: ["@js-temporal/polyfill", "decimal.js"],
    define: mode === "unchecked" ? { __METANO_CHECKS__: "false" } : {},
  });

  return output;
}

function moduleSource(benchCase: BenchCase, mode: Mode): string {
  const dist = DIST[mode];

  return `import { List } from "${dist}/list.js";
import { LegacyHashSet } from ${JSON.stringify(LEGACY_HASH_SET)};
import { Dictionary } from "${dist}/dictionary.js";
import { HashSet } from "${dist}/hash-set.js";
import { Queue } from "${dist}/queue.js";
import { Stack } from "${dist}/stack.js";
import { throwModified } from "${dist}/throw-helpers.js";

const N = ${N};
${benchCase.setup}
const calibrationArray = Array.from({ length: N }, (_, i) => i);
function calibration() { const c = calibrationArray; for (let i = 0; i < c.length; i++) c[i] = c[i] + 1; return c[0]; }
function baseline() { ${benchCase.baseline} }
function variant() { ${benchCase.variant} }

${RUNNER}`;
}

/** Times `kernel`: warm-up, then the median of the timed runs of CALLS calls each. */
const RUNNER = `
let sink = 0;
function timeKernel(kernel) {
  for (let run = 0; run < ${WARMUP_RUNS}; run++) for (let call = 0; call < ${CALLS}; call++) sink = (sink + (kernel() | 0)) | 0;
  const times = [];
  for (let run = 0; run < ${TIMED_RUNS}; run++) {
    const start = performance.now();
    for (let call = 0; call < ${CALLS}; call++) sink = (sink + (kernel() | 0)) | 0;
    times.push(performance.now() - start);
  }
  times.sort((x, y) => x - y);
  return times[${Math.floor(TIMED_RUNS / 2)}];
}
const role = process.argv[2];
const calibrationMs = timeKernel(calibration);
const medianMs = timeKernel(role === "baseline" ? baseline : variant);
console.log(JSON.stringify({ calibrationMs, medianMs, sink }));
`;

function measure(file: string, role: Role): Measurement {
  const result = spawnSync(process.execPath, [file, role], { encoding: "utf8", timeout: 300_000 });

  if (result.status !== 0) {
    throw new Error(`${file} ${role} failed: ${result.stderr || result.error?.message}`);
  }

  const lines = result.stdout.trim().split("\n");

  return JSON.parse(lines[lines.length - 1] ?? "{}") as Measurement;
}

function print(rows: readonly Row[]): void {
  console.log(`\n${engineVersion()} on ${machine()}`);
  console.log(`hot-helper shape: ${N} elements x ${CALLS} calls, median of ${TIMED_RUNS} after ${WARMUP_RUNS} warm-ups; C_ref = ${C_REF_NS[engine]} ns\n`);

  const width = Math.max(...rows.map((row) => row.name.length));

  console.log(`${"case".padEnd(width)}  ${"mode".padEnd(34)}  base ms  var ms  raw ns  C_meas  norm ns  budget`);

  for (const row of rows) {
    const verdict = row.budgetNs === undefined ? "report" : row.normalizedNs <= row.budgetNs ? `<= ${row.budgetNs} ok` : `> ${row.budgetNs} MISS${row.deferredTo === undefined ? "" : ` (${row.deferredTo})`}`;

    console.log(
      `${row.name.padEnd(width)}  ${MODE_LABEL[row.mode].padEnd(34)}  ${fixed(row.baselineMs, 7)}  ${fixed(row.variantMs, 6)}  ${fixed(row.rawNs, 6)}  ${fixed(row.cMeasuredNs, 6)}  ${fixed(row.normalizedNs, 7)}  ${verdict}`,
    );
  }
}

function fixed(value: number, width: number): string {
  return value.toFixed(2).padStart(width);
}

function engineVersion(): string {
  return engine === "bun" ? `Bun ${Bun.version}` : `Node.js ${process.versions.node}`;
}

function machine(): string {
  return `${cpus()[0]?.model ?? "unknown CPU"}, ${process.platform} ${process.arch}`;
}

interface Options {
  readonly filter: string;
  readonly check: boolean;
  readonly json: string | undefined;
}

function parseOptions(args: readonly string[]): Options {
  const valueOf = (flag: string) => {
    const position = args.indexOf(flag);

    return position >= 0 ? args[position + 1] : undefined;
  };

  return { filter: valueOf("--case") ?? "", check: args.includes("--check"), json: valueOf("--json") };
}

/** Runs every selected case, prints the table, writes the JSON report and applies `--check`. */
async function main(): Promise<void> {
  try {
    const rows = await runAll();

    print(rows);

    if (options.json !== undefined) {
      writeFileSync(options.json, `${JSON.stringify({ engine: engineVersion(), machine: machine(), rows }, null, 2)}\n`);
    }

    const misses = rows.filter((row) => row.mode === "checked" && row.deferredTo === undefined && row.budgetNs !== undefined && row.normalizedNs > row.budgetNs);

    if (options.check && misses.length > 0) {
      process.exitCode = 1;
    }
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

await main();
