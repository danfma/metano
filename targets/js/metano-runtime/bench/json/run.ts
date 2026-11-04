/**
 * JSON serializer benchmark (#456, #457): read and write times, and the
 * peak memory of one operation, of an `Order[]` payload on Bun and Node.
 * Every measurement runs in a fresh process.
 *
 * Usage: bun run bench:json [--sizes 100k,1m] [--memory 1m] [--runtimes bun,node]
 * Sizes: 1k, 100k, 1m, 10m, 100m. `--memory none` skips the memory probes.
 */
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { payloadFileOf } from "./order-payload.ts";

/** Fresh processes per memory measurement; the median is reported. */
const MEMORY_REPEATS = 3;

/** The operations of `memory-probe.ts`. */
const MEMORY_OPERATIONS = ["read", "read-text", "read-full", "write"];

const NODE_FLAGS = ["--expose-gc", "--experimental-transform-types", "--no-warnings"];

const options = parseOptions(process.argv.slice(2));

for (const size of [...options.sizes, ...options.memorySizes]) {
  payloadFileOf(size);
}

for (const runtime of options.runtimes) {
  for (const size of options.sizes) {
    const timing = probe<TimingResult>(runtime, "timing-probe.ts", [size]);

    for (const [name, ms] of Object.entries(timing.casesMs)) {
      console.log(
        `${timing.runtime.padEnd(14)} ${size.padStart(5)}  ${name.padEnd(32)} ${formatMs(ms)}`,
      );
    }
  }

  for (const size of options.memorySizes) {
    for (const operation of MEMORY_OPERATIONS) {
      const runs = Array.from({ length: MEMORY_REPEATS }, () =>
        probe<MemoryResult>(runtime, "memory-probe.ts", [size, operation]),
      );

      const median = runs.sort((a, b) => a.peakAfterMb - b.peakAfterMb)[MEMORY_REPEATS >> 1]!;

      console.log(
        `${median.runtime.padEnd(14)} ${size.padStart(5)}  ${operation.padEnd(10)} ` +
          `peak RSS ${median.peakAfterMb} MB (+${median.growthMb} MB over the loaded input)`,
      );
    }
  }
}

interface Options {
  readonly sizes: readonly string[];
  readonly memorySizes: readonly string[];
  readonly runtimes: readonly string[];
}

function parseOptions(args: readonly string[]): Options {
  const optionOf = (name: string, fallback: string) => {
    const position = args.indexOf(`--${name}`);
    return position >= 0 ? (args[position + 1] ?? fallback) : fallback;
  };

  const listOf = (name: string, fallback: string) =>
    optionOf(name, fallback)
      .split(",")
      .filter((item) => item !== "" && item !== "none");

  return {
    sizes: listOf("sizes", "100k,1m"),
    memorySizes: listOf("memory", "1m"),
    runtimes: listOf("runtimes", "bun,node"),
  };
}

interface TimingResult {
  readonly runtime: string;
  readonly casesMs: Readonly<Record<string, number>>;
}

interface MemoryResult {
  readonly runtime: string;
  readonly peakAfterMb: number;
  readonly growthMb: number;
}

/** Runs one probe in a fresh process; its last output line is its JSON result. */
function probe<T>(runtime: string, script: string, args: readonly string[]): T {
  const path = fileURLToPath(new URL(script, import.meta.url));
  const flags = runtime === "node" ? NODE_FLAGS : [];
  const run = spawnSync(runtime, [...flags, path, ...args], { encoding: "utf8" });

  if (run.status !== 0) {
    throw new Error(`${runtime} ${script} ${args.join(" ")} failed:\n${run.stderr}`);
  }

  return JSON.parse(run.stdout.trim().split("\n").at(-1)!) as T;
}

function formatMs(ms: number): string {
  return `${ms.toFixed(3).padStart(10)} ms`;
}
