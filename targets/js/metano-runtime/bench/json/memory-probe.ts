/**
 * Peak memory of ONE read or write of a payload, in a fresh process: the
 * peak RSS after loading the input, and after the operation. The load phase
 * is the same for both runtimes and every version of the serializer, so the
 * growth is comparable. Prints one JSON line.
 *
 * Usage: bun memory-probe.ts <size> <read|write>
 *        node --expose-gc --experimental-transform-types memory-probe.ts <size> <read|write>
 */
import { JsonSerializer } from "../../src/system/json/index.ts";
import { ordersSpec } from "./order-schema.ts";
import { payloadTextOf } from "./order-payload.ts";
import { collectGarbage, peakRssMb, runtimeName } from "./runtime-probe.ts";

const size = process.argv[2] ?? "10m";
const operation = process.argv[3] === "write" ? "write" : "read";
const input = { text: payloadTextOf(size) as string | null };

const orders =
  operation === "write" ? JsonSerializer.deserialize(JSON.parse(input.text!), ordersSpec) : null;

if (operation === "write") {
  input.text = null;
}

collectGarbage();

const before = peakRssMb();

const result =
  operation === "read"
    ? JsonSerializer.deserialize(JSON.parse(input.text!), ordersSpec)
    : JSON.stringify(JsonSerializer.serialize(orders!, ordersSpec));

const after = peakRssMb();

console.log(
  JSON.stringify({
    runtime: runtimeName(),
    size,
    operation,
    peakBeforeMb: before,
    peakAfterMb: after,
    growthMb: after - before,
    kept: result === null ? 0 : 1,
  }),
);
