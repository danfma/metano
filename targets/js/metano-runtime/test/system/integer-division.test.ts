import { describe, expect, test } from "bun:test";
import { intDiv } from "#/system/integer-division.ts";

/** Expected values were produced by C# `/` over `int` and `long` operands on .NET 10. */
const dotnetQuotients: [number, number, number][] = [
  [7, 2, 3],
  [-7, 2, -3],
  [7, -2, -3],
  [-7, -2, 3],
  [1, 3, 0],
  [-1, 3, 0],
  [0, 5, 0],
  [2147483647, 2, 1073741823],
  [-2147483648, 2, -1073741824],
  [9000000000, 4, 2250000000],
  [4000000000, 3, 1333333333],
];

describe("intDiv", () => {
  test.each(dotnetQuotients)("%p / %p truncates toward zero to %p", (dividend, divisor, quotient) => {
    expect(intDiv(dividend, divisor)).toBe(quotient);
  });

  test("throws the DivideByZeroException message on a zero divisor", () => {
    expect(() => intDiv(7, 0)).toThrow("Attempted to divide by zero.");
    expect(() => intDiv(0, 0)).toThrow("Attempted to divide by zero.");
  });

  test("yields null when either operand is null, like a lifted int? division", () => {
    expect(intDiv(null, 2)).toBeNull();
    expect(intDiv(7, null)).toBeNull();
    expect(intDiv(undefined, 0)).toBeNull();
  });
});
