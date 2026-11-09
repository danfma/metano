/**
 * The Node run of the spec 013 overload scenario (SC-006, R9): loads the
 * built `Estimate` module under Node — its static field is initialized
 * through a factory while the class body is still being evaluated — and
 * checks that the factories, the fast paths and the dispatchers return what
 * C# returns. Exits non-zero on any mismatch. Run with
 * `bun run build && bun run test:node`, which loads
 * `extensionless-imports.ts` for the generated relative specifiers.
 */

// Relative imports of the estimate modules alone: Node 24 rejects `#/`
// subpath specifiers, which the rest of the planning barrel uses.
import assert from "node:assert/strict";
import { BufferedEstimate } from "../../src/sample-issue-tracker/planning/domain/buffered-estimate.ts";
import { Estimate } from "../../src/sample-issue-tracker/planning/domain/estimate.ts";
import { plan, scale, total } from "../../src/sample-issue-tracker/planning/domain/estimate-math.ts";

const estimates = [Estimate.fromPoints(2), new BufferedEstimate(3, 1), Estimate.fromPoints(5)];

const checks: ReadonlyArray<readonly [string, string, string]> = [
  ["static field through a factory", Estimate.none.toString(), "0 pt"],
  ["factory", Estimate.fromPointsNote(3, "spike").toString(), "3 pt (spike)"],
  ["hand-written new", new Estimate(3).toString(), "3 pt"],
  ["fast path", Estimate.fromPoints(3).addPoints(2).toString(), "5 pt"],
  ["dispatcher", Estimate.fromPoints(3).add(Estimate.fromPoints(4)).toString(), "7 pt (combined)"],
  ["override through the dispatcher", new BufferedEstimate(3, 1).add(2).toString(), "6 pt"],
  ["virtual direct calls", total(estimates).toString(), "10 pt (combined)"],
  ["extension fast paths", plan(Estimate.fromPoints(3), 2).toString(), "6 pt (planned)"],
  ["extension dispatcher", scale(Estimate.fromPoints(3), 2).toString(), "6 pt"],
];

for (const [name, actual, expected] of checks) {
  assert.equal(actual, expected, name);
}

console.log(`overload scenario: ${checks.length} of ${checks.length} checks match .NET`);
