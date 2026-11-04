/**
 * Times the JSON reads and the write of one payload size: the median of warm
 * samples, with a full GC before each. Prints one JSON line.
 *
 * Reads (#457): the tree path (`JSON.parse` + `deserialize`) and the text
 * path (`deserializeText`), for the `Order` spec with decimals and without
 * them, and for a payload whose prices have 20+ significant digits, which
 * the text path reads exactly (the tree path rounds them). The write is
 * `serialize` + `JSON.stringify`.
 *
 * Usage: bun timing-probe.ts <size>
 *        node --expose-gc --experimental-transform-types timing-probe.ts <size>
 */
import { JsonSerializer } from "../../src/system/json/index.ts";
import { ordersSpec, ordersWithoutDecimalsSpec } from "./order-schema.ts";
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
const fullText = payloadTextOf(size, "full");
const orders = JsonSerializer.deserialize(JSON.parse(text), ordersSpec);

/** Each case by name, in the order the report prints them. */
const CASES: Readonly<Record<string, () => unknown>> = {
  "read tree, decimals": () => JsonSerializer.deserialize(JSON.parse(text), ordersSpec),
  "read text, decimals": () => JsonSerializer.deserializeText(text, ordersSpec),
  "read tree, no decimals": () =>
    JsonSerializer.deserialize(JSON.parse(text), ordersWithoutDecimalsSpec),
  "read text, no decimals": () => JsonSerializer.deserializeText(text, ordersWithoutDecimalsSpec),
  "read tree, 20+ digits (inexact)": () =>
    JsonSerializer.deserialize(JSON.parse(fullText), ordersSpec),
  "read text, 20+ digits (exact)": () => JsonSerializer.deserializeText(fullText, ordersSpec),
  write: () => JSON.stringify(JsonSerializer.serialize(orders, ordersSpec)),
};

const casesMs = Object.fromEntries(
  Object.entries(CASES).map(([name, operation]) => [name, medianMs(operation)]),
);

console.log(
  JSON.stringify({ runtime: runtimeName(), size, bytes: Buffer.byteLength(text), casesMs }),
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
