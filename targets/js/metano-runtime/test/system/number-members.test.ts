import { describe, expect, test } from "bun:test";
import { FormatException } from "#/system/exceptions/index.ts";
import { compareNumber, convertStringToDouble } from "#/system/number-members.ts";

describe("compareNumber", () => {
  test.each([
    [1, 2, -1],
    [2, 2, 0],
    [3, 2, 1],
    [-0, 0, 0],
    [Number.NaN, 1, -1],
    [1, Number.NaN, 1],
    [Number.NaN, Number.NaN, 0],
    [Number.NEGATIVE_INFINITY, Number.MIN_SAFE_INTEGER, -1],
  ])("compares %p with %p as %p", (value, other, expected) => {
    expect(compareNumber(value, other)).toBe(expected);
  });
});

describe("convertStringToDouble", () => {
  test("converts null to 0", () => {
    expect(convertStringToDouble(null)).toBe(0);
  });

  test("parses text as double.Parse does", () => {
    expect(convertStringToDouble("1.5")).toBe(1.5);
  });

  test("throws FormatException for text that is no number", () => {
    expect(() => convertStringToDouble("abc")).toThrow(FormatException);
  });
});
