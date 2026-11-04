/**
 * Times a JSON read (`JSON.parse` + `JsonSerializer.deserialize`) and write
 * (`JsonSerializer.serialize` + `JSON.stringify`) of one payload: the median
 * of warm samples, with a full GC before each. Prints one JSON line.
 *
 * Usage: bun timing-probe.ts <size>
 *        node --expose-gc --experimental-transform-types timing-probe.ts <size>
 */
import { JsonSerializer } from "../../src/system/json/index.ts";
import { ordersSpec } from "./order-schema.ts";
import { payloadTextOf } from "./order-payload.ts";
import { collectGarbage, runtimeName } from "./runtime-probe.ts";

/** Calls per sample, samples, and warm-up rounds by payload size. */
const SAMPLING: Readonly<
  Record<string, readonly [batch: number, samples: number, warmUp: number]>
> = {
  "1k": [2000, 15, 5],
  "100k": [20, 15, 5],
  "1m": [3, 11, 3],
  "10m": [1, 9, 3],
  "100m": [1, 5, 2],
};

const size = process.argv[2] ?? "100k";
const [batch, samples, warmUp] = SAMPLING[size] ?? [1, 5, 2];
const text = payloadTextOf(size);
const orders = JsonSerializer.deserialize(JSON.parse(text), ordersSpec);

const read = () => JsonSerializer.deserialize(JSON.parse(text), ordersSpec);
const write = () => JSON.stringify(JsonSerializer.serialize(orders, ordersSpec));

console.log(
  JSON.stringify({
    runtime: runtimeName(),
    size,
    bytes: Buffer.byteLength(text),
    readMs: medianMs(read),
    writeMs: medianMs(write),
  }),
);

function medianMs(operation: () => unknown): number {
  for (let round = 0; round < warmUp * batch; round++) {
    operation();
  }

  const durations = Array.from({ length: samples }, () => sampleMs(operation)).sort(
    (a, b) => a - b,
  );

  return Number(durations[durations.length >> 1]!.toFixed(4));
}

function sampleMs(operation: () => unknown): number {
  collectGarbage();

  const start = performance.now();

  for (let call = 0; call < batch; call++) {
    operation();
  }

  return (performance.now() - start) / batch;
}
