/**
 * Peak memory of ONE read or write of a payload, in a fresh process: the
 * peak RSS after loading the input, and after the operation. The load phase
 * is the same for both runtimes and every version of the serializer, so the
 * growth is comparable. Prints one JSON line.
 *
 * Operations: `read` (`JSON.parse` + `deserialize`), `read-text`
 * (`deserializeText`), `read-full` (`deserializeText` of the payload whose
 * prices have 20+ significant digits, the exact tokenizer path, #457) and
 * `write`.
 *
 * Usage: bun memory-probe.ts <size> <operation>
 *        node --expose-gc --experimental-transform-types memory-probe.ts <size> <operation>
 */
import { JsonSerializer } from "../../src/system/json/index.ts";
import { ordersSpec } from "./order-schema.ts";
import { payloadTextOf } from "./order-payload.ts";
import { collectGarbage, peakRssMb, runtimeName } from "./runtime-probe.ts";

const size = process.argv[2] ?? "10m";
const operation = process.argv[3] ?? "read";

const OPERATIONS: Readonly<Record<string, () => Measurement>> = {
  read: measureRead,
  "read-text": () => measureTextRead(payloadTextOf(size)),
  "read-full": () => measureTextRead(payloadTextOf(size, "full")),
  write: measureWrite,
};

const measureOperation = OPERATIONS[operation];

if (!measureOperation) {
  throw new Error(`Unknown operation '${operation}'. Known: ${Object.keys(OPERATIONS)}.`);
}

const measurement = measureOperation();

console.log(JSON.stringify({ runtime: runtimeName(), size, operation, ...measurement }));

interface Measurement {
  readonly peakBeforeMb: number;
  readonly peakAfterMb: number;
  readonly growthMb: number;
}

/** Reads the payload text; the text stays alive, as it would in a caller. */
function measureRead(): Measurement {
  const text = payloadTextOf(size);

  return measure(() => JsonSerializer.deserialize(JSON.parse(text), ordersSpec));
}

/** Reads `text` through `deserializeText`; the text stays alive, as it would in a caller. */
function measureTextRead(text: string): Measurement {
  return measure(() => JsonSerializer.deserializeText(text, ordersSpec));
}

/** Writes the orders read from the payload; the text is out of scope before measuring. */
function measureWrite(): Measurement {
  const orders = JsonSerializer.deserialize(JSON.parse(payloadTextOf(size)), ordersSpec);

  return measure(() => JSON.stringify(JsonSerializer.serialize(orders, ordersSpec)));
}

/** The peak RSS around `operation`, after a full collection of the load phase's garbage. */
function measure(operation: () => unknown): Measurement {
  collectGarbage();

  const peakBeforeMb = peakRssMb();
  const result = operation();
  const peakAfterMb = peakRssMb();

  // Keeps the result alive until the peak is taken.
  if (result === undefined) {
    throw new Error("The operation returned nothing.");
  }

  return { peakBeforeMb, peakAfterMb, growthMb: peakAfterMb - peakBeforeMb };
}
