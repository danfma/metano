/**
 * Runs the 64-bit benchmark (spec 012 FR-066) on Bun and Node, one fresh
 * process per runtime, and prints one table per runtime with the ratio to
 * the `number` baseline — the figure the docs record.
 *
 * Usage: bun run bench:int64 [--runtimes bun,node]
 */
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const NODE_FLAGS = ["--expose-gc", "--experimental-transform-types", "--no-warnings"];

const options = parseOptions(process.argv.slice(2));

for (const runtime of options.runtimes) {
  const probe = probeResult(runtime);

  const cases = Object.entries(probe.casesMs);
  const width = Math.max(...cases.map(([name]) => name.length));

  console.log(`\n${probe.runtime} — median of 10 runs after 3 warm-ups`);

  for (const [name, ms] of cases) {
    console.log(`  ${name.padEnd(width)}  ${formatMs(ms)}`);
  }

  for (const operation of new Set(cases.map(([name]) => operationOf(name)))) {
    const bigint = cases.find(([name]) => name.startsWith(operation) && name.includes("bigint"))![1];
    const number = cases.find(([name]) => name.startsWith(operation) && name.includes("number"))![1];

    console.log(
      `  ${`${operation} ratio bigint/number`.padEnd(width)}  ${formatRatio(bigint / number)}`,
    );
  }
}

interface Options {
  readonly runtimes: readonly string[];
}

function parseOptions(args: readonly string[]): Options {
  const position = args.indexOf("--runtimes");

  const runtimes =
    position >= 0
      ? (args[position + 1] ?? "bun,node").split(",").filter((item) => item !== "")
      : ["bun", "node"];

  return { runtimes };
}

interface ProbeResult {
  readonly runtime: string;
  readonly casesMs: Readonly<Record<string, number>>;
}

/** Runs one probe in a fresh process; its last output line is its JSON result. */
function probeResult(runtime: string): ProbeResult {
  const path = fileURLToPath(new URL("./int64-probe.ts", import.meta.url));
  const flags = runtime === "node" ? NODE_FLAGS : [];
  const run = spawnSync(runtime, [...flags, path], { encoding: "utf8" });

  if (run.status !== 0) {
    throw new Error(`${runtime} int64-probe failed:\n${run.stderr}`);
  }

  return JSON.parse(run.stdout.trim().split("\n").at(-1)!) as ProbeResult;
}

/** A case name without its ` — bigint` / ` — number` suffix. */
function operationOf(caseName: string): string {
  return caseName.slice(0, caseName.lastIndexOf(" — "));
}

function formatMs(ms: number): string {
  return `${ms.toFixed(2).padStart(10)} ms`;
}

function formatRatio(ratio: number): string {
  const text = ratio < 1 ? ratio.toFixed(2) : ratio.toFixed(1);

  return `${text}x`;
}
