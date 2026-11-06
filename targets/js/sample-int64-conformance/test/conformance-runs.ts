/**
 * The fixtures of the spec 012 reference conformance and the transpiled twin
 * modules each runs against, shared by the bun test and the Node run so the
 * two cannot drift apart. Relative imports: Node 24 rejects `#/` subpath
 * specifiers.
 */

import * as conversions from "../src/sample-int64-conformance/conversions.ts";
import { DecimalConversions } from "../src/sample-int64-conformance/conversions.ts";
import * as division from "../src/sample-int64-conformance/division.ts";
import * as overflow from "../src/sample-int64-conformance/overflow.ts";
import * as shifts from "../src/sample-int64-conformance/shifts.ts";

import type { Twin } from "./conformance.ts";

export { loadFixture, mismatchesOf } from "./conformance.ts";

/** One fixture file and the twin it runs against. */
export type FixtureRun = { readonly name: string; readonly twin: Twin };

export const FIXTURE_RUNS: readonly FixtureRun[] = [
  { name: "operators.json", twin: [overflow, division, shifts] },
  { name: "conversions.json", twin: [conversions, DecimalConversions] },
];
