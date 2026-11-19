/**
 * Spec 011 FR-054's budget for the immutable classes (stage 011h, research
 * R18, W4-11): building 1e4 elements by repeated `Add` (`x = x.add(i)`) takes
 * at most 50 ms on the reference machine, normalized as NFR-001 does by the
 * calibration kernel (`a[i] = a[i] + 1` over a native array, 500 passes of
 * 1e4 elements), on Node.js and Bun. Reports the raw and normalized medians of
 * 11 timed runs after 5 warm-up runs, against the checked dist.
 *
 * Usage: `bun run bench:immutable` (Bun) / `bun run bench:immutable:node` (Node.js)
 */
import { ImmutableDictionary } from "../dist/system/collections/immutable-dictionary.js";
import { ImmutableHashSet } from "../dist/system/collections/immutable-hash-set.js";
import { ImmutableList } from "../dist/system/collections/immutable-list.js";

const N = 10_000;
const BUDGET_MS = 50;
const WARMUP_RUNS = 5;
const TIMED_RUNS = 11;
const CALIBRATION_PASSES = 500;

/** Research R17's reference machine: Apple M1 Pro, Node.js 24.11, Bun 1.4.2. */
const C_REF_NS = { node: 0.59, bun: 0.47 } as const;

const engine = typeof Bun === "undefined" ? "node" : "bun";

let sink = 0;

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);

  return sorted[Math.floor(sorted.length / 2)] as number;
}

function timeMs(run: () => number): number {
  for (let warmup = 0; warmup < WARMUP_RUNS; warmup++) {
    sink += run();
  }

  const times: number[] = [];

  for (let timed = 0; timed < TIMED_RUNS; timed++) {
    const start = performance.now();

    sink += run();
    times.push(performance.now() - start);
  }

  return median(times);
}

const calibrationArray = Array.from({ length: N }, (_, index) => index);

function calibration(): number {
  const array = calibrationArray;

  for (let pass = 0; pass < CALIBRATION_PASSES; pass++) {
    for (let index = 0; index < array.length; index++) {
      array[index] = (array[index] as number) + 1;
    }
  }

  return array[0] as number;
}

const cases: [string, () => number][] = [
  [
    "ImmutableList",
    () => {
      let list = ImmutableList.empty<number>();

      for (let index = 0; index < N; index++) {
        list = list.add(index);
      }

      return list.count;
    },
  ],
  [
    "ImmutableHashSet",
    () => {
      let set = ImmutableHashSet.empty<number>();

      for (let index = 0; index < N; index++) {
        set = set.add(index);
      }

      return set.count;
    },
  ],
  [
    "ImmutableDictionary",
    () => {
      let dictionary = ImmutableDictionary.empty<number, number>();

      for (let index = 0; index < N; index++) {
        dictionary = dictionary.add(index, index);
      }

      return dictionary.count;
    },
  ],
];

const cMeasuredNs = (timeMs(calibration) * 1e6) / (CALIBRATION_PASSES * N);
const version = typeof Bun === "undefined" ? process.version : `bun ${Bun.version}`;

console.log(`${engine} (${version}); C_measured ${cMeasuredNs.toFixed(3)} ns (C_ref ${C_REF_NS[engine]} ns)`);

let failed = false;

for (const [name, run] of cases) {
  const rawMs = timeMs(run);
  const normalizedMs = (rawMs * C_REF_NS[engine]) / cMeasuredNs;
  const verdict = normalizedMs <= BUDGET_MS ? "ok" : "OVER";

  failed ||= normalizedMs > BUDGET_MS;
  console.log(`${name}: 1e4 repeated Add ${rawMs.toFixed(2)} ms raw, ${normalizedMs.toFixed(2)} ms normalized (budget ${BUDGET_MS} ms) ${verdict}`);
}

console.log(`sink ${sink}`);

if (failed && process.argv.includes("--check")) {
  process.exit(1);
}
