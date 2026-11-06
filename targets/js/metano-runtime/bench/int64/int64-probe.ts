/**
 * Times the 64-bit cases of int64-cases.ts and prints one JSON line: the
 * median of 10 runs after 3 warm-ups, per case and per side (spec 012
 * FR-066). One fresh process per runtime — run.ts drives it.
 *
 * Usage: bun int64-probe.ts
 *        node --experimental-transform-types int64-probe.ts
 */
import { collectGarbage, runtimeName } from "../json/runtime-probe.ts";

import { INT64_CASES } from "./int64-cases.ts";

const SAMPLES = 10;

const WARM_UPS = 3;

const casesMs = Object.fromEntries(
  INT64_CASES.flatMap((operation) => [
    [`${operation.name} — bigint`, medianMs(operation.withInt64)],
    [`${operation.name} — number`, medianMs(operation.withNumber)],
  ]),
);

console.log(JSON.stringify({ runtime: runtimeName(), casesMs }));

function medianMs(operation: () => unknown): number {
  for (let round = 0; round < WARM_UPS; round++) {
    operation();
  }

  const durations = Array.from({ length: SAMPLES }, () => sampleMs(operation)).sort(
    (a, b) => a - b,
  );

  return Number(durations[durations.length >> 1]!.toFixed(2));
}

function sampleMs(operation: () => unknown): number {
  collectGarbage();

  const start = performance.now();

  operation();

  return performance.now() - start;
}
