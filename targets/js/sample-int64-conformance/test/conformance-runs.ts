/**
 * The fixtures of the spec 012 reference conformance and the transpiled twin
 * modules each runs against, shared by the bun test and the Node run so the
 * two cannot drift apart. Relative imports: Node 24 rejects `#/` subpath
 * specifiers.
 */

import * as aggregates from "../src/sample-int64-conformance/aggregates.ts";
import * as bigIntegerConversions from "../src/sample-int64-conformance/big-integer-conversions.ts";
import { DecimalBigIntegerConversions } from "../src/sample-int64-conformance/big-integer-conversions.ts";
import * as comparisons from "../src/sample-int64-conformance/comparisons.ts";
import * as convertCalls from "../src/sample-int64-conformance/convert-calls.ts";
import { DecimalConvertCalls } from "../src/sample-int64-conformance/convert-calls.ts";
import * as conversions from "../src/sample-int64-conformance/conversions.ts";
import { DecimalConversions } from "../src/sample-int64-conformance/conversions.ts";
import * as division from "../src/sample-int64-conformance/division.ts";
import * as formatting from "../src/sample-int64-conformance/formatting.ts";
import * as indexing from "../src/sample-int64-conformance/indexing.ts";
import * as mathStatics from "../src/sample-int64-conformance/math-statics.ts";
import * as nullableUnwraps from "../src/sample-int64-conformance/nullable-unwraps.ts";
import * as overflow from "../src/sample-int64-conformance/overflow.ts";
import * as parsing from "../src/sample-int64-conformance/parsing.ts";
import * as shifts from "../src/sample-int64-conformance/shifts.ts";
import * as tupleConversions from "../src/sample-int64-conformance/tuple-conversions.ts";

import type { Twin } from "./conformance.ts";

export { loadFixture, mismatchesOf } from "./conformance.ts";

/** One fixture file and the twin it runs against. */
export type FixtureRun = { readonly name: string; readonly twin: Twin };

export const FIXTURE_RUNS: readonly FixtureRun[] = [
  { name: "operators.json", twin: [overflow, division, shifts] },
  {
    name: "conversions.json",
    twin: [conversions, DecimalConversions, bigIntegerConversions, DecimalBigIntegerConversions],
  },
  { name: "math.json", twin: [mathStatics] },
  { name: "aggregates.json", twin: [aggregates] },
  {
    name: "formatting.json",
    twin: [formatting, parsing, convertCalls, DecimalConvertCalls],
  },
  { name: "comparisons.json", twin: [comparisons] },
  { name: "indexing.json", twin: [indexing] },
  { name: "tuples.json", twin: [tupleConversions] },
  { name: "nullable-unwraps.json", twin: [nullableUnwraps] },
];
