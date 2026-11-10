/**
 * Runs the spec 012 reference fixtures (FR-064) against the transpiled twin
 * under bun; `bun run test:node` runs the same cases under Node, because bun
 * hides some load-order issues.
 */

import { describe, expect, test } from "bun:test";

// Relative import: Node 24 rejects `#/` subpath specifiers.
import * as conversions from "../src/sample-int64-conformance/conversions.ts";

import { FIXTURE_RUNS, loadFixture, mismatchesOf } from "./conformance-runs.ts";

describe("the transpiled twin matches .NET", () => {
  test.each(FIXTURE_RUNS.map((run) => [run.name, run] as const))("%s", async (_, {
    name,
    twin,
  }) => {
    const fixture = loadFixture(name);

    expect(fixture.cases.length).toBeGreaterThan(0);
    expect(await mismatchesOf(fixture, twin)).toEqual([]);
  });
});

/**
 * A fractional `number` that reaches an `int` slot from untyped JS is the
 * interop hazard spec 012 names (Edge Cases): the next widening throws
 * instead of producing a value C# could not hold.
 */
test("a fractional number widened to long throws RangeError", () => {
  expect(() => conversions.fromInt(3.7)).toThrow(RangeError);
});
