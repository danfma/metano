import { describe, expect, test } from "bun:test";
import {
  type DecimalRoundable,
  MidpointRounding,
  roundDecimal,
  roundNumber,
} from "#/system/math.ts";

/**
 * Expected values were produced by `Math.Round(value, digits, mode)` on .NET 10. The
 * helper was also compared bit for bit against .NET on ~300k random and midpoint inputs
 * (every mode, digits 0–3 and 15) while it was written.
 */
const dotnetRoundCases: [number, number, MidpointRounding, number][] = [
  [0.5, 0, MidpointRounding.ToEven, 0],
  [0.5, 0, MidpointRounding.AwayFromZero, 1],
  [0.5, 0, MidpointRounding.ToZero, 0],
  [0.5, 0, MidpointRounding.ToNegativeInfinity, 0],
  [0.5, 0, MidpointRounding.ToPositiveInfinity, 1],
  [1.5, 0, MidpointRounding.ToEven, 2],
  [1.5, 0, MidpointRounding.AwayFromZero, 2],
  [1.5, 0, MidpointRounding.ToZero, 1],
  [1.5, 0, MidpointRounding.ToNegativeInfinity, 1],
  [1.5, 0, MidpointRounding.ToPositiveInfinity, 2],
  [2.5, 0, MidpointRounding.ToEven, 2],
  [2.5, 0, MidpointRounding.AwayFromZero, 3],
  [2.5, 0, MidpointRounding.ToZero, 2],
  [2.5, 0, MidpointRounding.ToNegativeInfinity, 2],
  [2.5, 0, MidpointRounding.ToPositiveInfinity, 3],
  [-0.5, 0, MidpointRounding.ToEven, -0],
  [-0.5, 0, MidpointRounding.AwayFromZero, -1],
  [-0.5, 0, MidpointRounding.ToZero, -0],
  [-0.5, 0, MidpointRounding.ToNegativeInfinity, -1],
  [-0.5, 0, MidpointRounding.ToPositiveInfinity, -0],
  [-2.5, 0, MidpointRounding.ToEven, -2],
  [-2.5, 0, MidpointRounding.AwayFromZero, -3],
  [-2.5, 0, MidpointRounding.ToZero, -2],
  [-2.5, 0, MidpointRounding.ToNegativeInfinity, -3],
  [-2.5, 0, MidpointRounding.ToPositiveInfinity, -2],
  [-0.4, 0, MidpointRounding.ToEven, -0],
  [-0.4, 0, MidpointRounding.AwayFromZero, -0],
  [-0.4, 0, MidpointRounding.ToZero, -0],
  [-0.4, 0, MidpointRounding.ToNegativeInfinity, -1],
  [-0.4, 0, MidpointRounding.ToPositiveInfinity, -0],
  [2.675, 2, MidpointRounding.ToEven, 2.68],
  [2.675, 2, MidpointRounding.AwayFromZero, 2.68],
  [2.675, 2, MidpointRounding.ToZero, 2.67],
  [2.675, 2, MidpointRounding.ToNegativeInfinity, 2.67],
  [2.675, 2, MidpointRounding.ToPositiveInfinity, 2.68],
  [1.005, 2, MidpointRounding.ToEven, 1],
  [1.005, 2, MidpointRounding.AwayFromZero, 1],
  [1.005, 2, MidpointRounding.ToZero, 1],
  [1.005, 2, MidpointRounding.ToNegativeInfinity, 1],
  [1.005, 2, MidpointRounding.ToPositiveInfinity, 1.01],
  [2.345, 2, MidpointRounding.ToEven, 2.35],
  [2.345, 2, MidpointRounding.AwayFromZero, 2.35],
  [2.345, 2, MidpointRounding.ToZero, 2.34],
  [2.345, 2, MidpointRounding.ToNegativeInfinity, 2.34],
  [2.345, 2, MidpointRounding.ToPositiveInfinity, 2.35],
  [1234.5678, 2, MidpointRounding.ToEven, 1234.57],
  [1234.5678, 2, MidpointRounding.AwayFromZero, 1234.57],
  [1234.5678, 2, MidpointRounding.ToZero, 1234.56],
  [1234.5678, 2, MidpointRounding.ToNegativeInfinity, 1234.56],
  [1234.5678, 2, MidpointRounding.ToPositiveInfinity, 1234.57],
  [0.49999999999999994, 0, MidpointRounding.ToEven, 0],
  [0.49999999999999994, 0, MidpointRounding.AwayFromZero, 0],
  [0.49999999999999994, 0, MidpointRounding.ToZero, 0],
  [0.49999999999999994, 0, MidpointRounding.ToNegativeInfinity, 0],
  [0.49999999999999994, 0, MidpointRounding.ToPositiveInfinity, 1],
  [4503599627370495.5, 0, MidpointRounding.ToEven, 4503599627370496],
  [4503599627370495.5, 0, MidpointRounding.AwayFromZero, 4503599627370496],
  [4503599627370495.5, 0, MidpointRounding.ToZero, 4503599627370495],
  [4503599627370495.5, 0, MidpointRounding.ToNegativeInfinity, 4503599627370495],
  [4503599627370495.5, 0, MidpointRounding.ToPositiveInfinity, 4503599627370496],
];

describe("roundNumber", () => {
  test.each(dotnetRoundCases)(
    "Math.Round(%p, %p, mode %p) is %p",
    (value, digits, mode, expected) => {
      expect(Object.is(roundNumber(value, digits, mode), expected)).toBe(true);
    },
  );

  test("defaults to zero digits and banker's rounding, like Math.Round(x)", () => {
    expect(roundNumber(0.5)).toBe(0);
    expect(roundNumber(1.5)).toBe(2);
    expect(roundNumber(2.5)).toBe(2);
    expect(roundNumber(-2.5)).toBe(-2);
    expect(roundNumber(3.5)).toBe(4);
  });

  test("rounds to digits with the default mode, like Math.Round(x, digits)", () => {
    expect(roundNumber(2.675, 2)).toBe(2.68);
    expect(roundNumber(0.125, 2)).toBe(0.12);
    expect(roundNumber(0.375, 2)).toBe(0.38);
  });

  test("keeps the sign of a zero result", () => {
    expect(Object.is(roundNumber(-0.4), -0)).toBe(true);
    expect(Object.is(roundNumber(-0.004, 2), -0)).toBe(true);
    expect(Object.is(roundNumber(0.4), 0)).toBe(true);
  });

  test("returns NaN, infinities and magnitudes of 1e16 or more unchanged", () => {
    expect(roundNumber(Number.NaN)).toBeNaN();
    expect(roundNumber(Infinity, 2)).toBe(Infinity);
    expect(roundNumber(-Infinity)).toBe(-Infinity);
    expect(roundNumber(1e16 + 2, 3)).toBe(1e16 + 2);
  });

  test("rejects digit counts and modes that .NET rejects", () => {
    expect(() => roundNumber(1, -1)).toThrow(RangeError);
    expect(() => roundNumber(1, 16)).toThrow(RangeError);
    expect(() => roundNumber(1, 1.5)).toThrow(RangeError);
    expect(() => roundNumber(1, 0, 5 as MidpointRounding)).toThrow(RangeError);
  });
});

/**
 * Records the `toDecimalPlaces` call so the tests pin the decimal.js rounding constant
 * each mode selects without the runtime depending on decimal.js.
 */
class RecordingDecimal implements DecimalRoundable<RecordingDecimal> {
  calls: [number, number][] = [];

  toDecimalPlaces(decimalPlaces: number, rounding: number): RecordingDecimal {
    this.calls.push([decimalPlaces, rounding]);

    return this;
  }
}

describe("roundDecimal", () => {
  const roundingFor = (digits?: number, mode?: MidpointRounding) =>
    roundDecimal(new RecordingDecimal(), digits, mode).calls;

  test("defaults to zero digits and ROUND_HALF_EVEN, like Math.Round(decimal)", () => {
    expect(roundingFor()).toEqual([[0, 6]]);
    expect(roundingFor(2)).toEqual([[2, 6]]);
  });

  test("maps each MidpointRounding mode to its decimal.js constant", () => {
    expect(roundingFor(1, MidpointRounding.ToEven)).toEqual([[1, 6]]);
    expect(roundingFor(1, MidpointRounding.AwayFromZero)).toEqual([[1, 4]]);
    expect(roundingFor(1, MidpointRounding.ToZero)).toEqual([[1, 1]]);
    expect(roundingFor(1, MidpointRounding.ToNegativeInfinity)).toEqual([[1, 3]]);
    expect(roundingFor(1, MidpointRounding.ToPositiveInfinity)).toEqual([[1, 2]]);
  });

  test("rejects digit counts and modes that .NET rejects", () => {
    expect(() => roundingFor(29)).toThrow(RangeError);
    expect(() => roundingFor(0, -1 as MidpointRounding)).toThrow(RangeError);
  });
});
