import { describe, expect, test } from "bun:test";
import {
  BufferedEstimate,
  Estimate,
  plan,
  scale,
  scaleFactor,
  scaleNote,
  total,
} from "#/sample-issue-tracker/planning/domain";

/**
 * The overload-factories scenario of spec 013 (FR-021, SC-006): the
 * generated `Estimate` constructs through its factories and binds its fast
 * paths directly, while hand-written TypeScript can use either the
 * addressable members or the runtime dispatchers.
 */
describe("Estimate overload factories", () => {
  test("the static field is initialized through a factory before the constructors", () => {
    expect(Estimate.none.points).toBe(0);
    expect(Estimate.none.toString()).toBe("0 pt");
  });

  test("each constructor overload has its own factory", () => {
    expect(Estimate.fromPoints(3).toString()).toBe("3 pt");
    expect(Estimate.fromPointsNote(3, "spike").toString()).toBe("3 pt (spike)");

    const combined = Estimate.fromFirstSecond(Estimate.fromPoints(2), Estimate.fromPoints(5));

    expect(combined.toString()).toBe("7 pt (combined)");
  });

  test("a hand-written new keeps the runtime-dispatched constructor", () => {
    expect(new Estimate(3).toString()).toBe("3 pt");
    expect(new Estimate(3, "spike").toString()).toBe("3 pt (spike)");
    expect(new Estimate(new Estimate(1), new Estimate(2)).points).toBe(3);
  });
});

describe("Estimate fast paths", () => {
  test("the fast paths and the dispatcher return the same results", () => {
    const estimate = Estimate.fromPoints(3);

    expect(estimate.addPoints(2).points).toBe(5);
    expect(estimate.add(2).points).toBe(5);
    expect(estimate.addOther(Estimate.fromPoints(4)).points).toBe(7);
    expect(estimate.add(Estimate.fromPoints(4)).points).toBe(7);
  });

  test("an override of one overload is reached through the fast path and the dispatcher", () => {
    const buffered = new BufferedEstimate(3, 1);

    expect(buffered.addPoints(2).points).toBe(6);
    expect(buffered.add(2).points).toBe(6);
    expect(buffered.addOther(Estimate.fromPoints(4)).points).toBe(7);
  });

  test("generated calls bind the overload C# selected, virtually", () => {
    const estimates = [Estimate.fromPoints(2), new BufferedEstimate(3, 1), Estimate.fromPoints(5)];

    expect(total(estimates).toString()).toBe("10 pt (combined)");
  });
});

describe("Estimate extension group", () => {
  test("the module fast paths and the dispatcher return the same results", () => {
    const estimate = Estimate.fromPointsNote(3, "spike");

    expect(scaleFactor(estimate, 2).toString()).toBe("6 pt (spike)");
    expect(scale(estimate, 2).toString()).toBe("6 pt (spike)");
    expect(scaleNote(estimate, "story").toString()).toBe("3 pt (story)");
    expect(scale(estimate, "story").toString()).toBe("3 pt (story)");
  });

  test("a generated module function calls the extension fast paths", () => {
    expect(plan(Estimate.fromPoints(3), 2).toString()).toBe("6 pt (planned)");
  });
});
