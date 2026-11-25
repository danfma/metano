import { describe, expect, test } from "bun:test";
import { DivideByZeroException, OverflowException } from "#/system/exceptions/index.ts";
import { intDiv, intRem } from "#/system/integer-division.ts";

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
  test.each(
    dotnetQuotients,
  )("%p / %p truncates toward zero to %p", (dividend, divisor, quotient) => {
    expect(intDiv(dividend, divisor)).toBe(quotient);
  });

  test("throws a DivideByZeroException with .NET's message on a zero divisor", () => {
    expect(() => intDiv(7, 0)).toThrow(DivideByZeroException);
    expect(() => intDiv(7, 0)).toThrow("Attempted to divide by zero.");
    expect(() => intDiv(0, 0)).toThrow("Attempted to divide by zero.");
  });

  test("yields null when either operand is null, like a lifted int? division", () => {
    expect(intDiv(null, 2)).toBeNull();
    expect(intDiv(7, null)).toBeNull();
    expect(intDiv(undefined, 0)).toBeNull();
  });
});

describe("intDiv overflow", () => {
  test("throws an OverflowException for int.MinValue / -1, as .NET does", () => {
    expect(() => intDiv(-2147483648, -1)).toThrow(OverflowException);
  });
});

/** Expected values were produced by C# `%` over `int` operands on .NET 10. */
const dotnetRemainders: [number, number, number][] = [
  [7, 3, 1],
  [-7, 2, -1],
  [7, -2, 1],
  [-4, 2, 0],
  [7, -1, 0],
  [-2147483648, 2, 0],
];

describe("intRem", () => {
  test.each(dotnetRemainders)("%p %% %p is %p", (dividend, divisor, remainder) => {
    expect(Object.is(intRem(dividend, divisor), remainder)).toBe(true);
  });

  test("throws what .NET throws for a zero divisor and int.MinValue % -1", () => {
    expect(() => intRem(7, 0)).toThrow("Attempted to divide by zero.");
    expect(() => intRem(-2147483648, -1)).toThrow(OverflowException);
  });

  test("yields null when either operand is null, like a lifted int? remainder", () => {
    expect(intRem(null, 2)).toBeNull();
    expect(intRem(7, undefined)).toBeNull();
  });
});
