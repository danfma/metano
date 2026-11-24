import { describe, expect, test } from "bun:test";
import { Decimal } from "#/system/decimal";
import { decimalTextFromDouble, parseDecimalText } from "#/system/decimal-parsing.ts";

import {
  ArgumentNullException,
  FormatException,
  OverflowException,
} from "#/system/exceptions/index.ts";

/** Outcomes printed by .NET 10's `decimal.Parse(text)` in the invariant culture. */
describe("parseDecimalText", () => {
  test.each([
    ["1.5", "1.5"],
    [" -1,234.50 ", "-1234.50"],
    ["5-", "-5"],
    [".5", "0.5"],
    ["79228162514264337593543950335", "79228162514264337593543950335"],
  ])("%p is %p", (text, expected) => {
    expect(parseDecimalText(text)).toBe(expected);
  });

  test("rejects what .NET rejects", () => {
    expect(() => parseDecimalText("zz")).toThrow(FormatException);

    expect(() => parseDecimalText("zz")).toThrow(
      "The input string 'zz' was not in a correct format.",
    );

    expect(() => parseDecimalText("1e5")).toThrow(FormatException);
    expect(() => parseDecimalText("-5-")).toThrow(FormatException);
    expect(() => parseDecimalText("99999999999999999999999999999999")).toThrow(OverflowException);
    expect(() => parseDecimalText(null)).toThrow(ArgumentNullException);
  });
});

/** Expected values were produced by `(decimal)d` and `(decimal)f` on .NET 10. */
describe("decimalTextFromDouble", () => {
  test.each([
    [0.1 + 0.2, 15, "0.3"],
    [1 / 3, 15, "0.333333333333333"],
    [Math.fround(1.1), 7, "1.1"],
    [-2.5, 15, "-2.5"],
    [123456789012345680000, 15, "123456789012346000000"],
  ] as const)("(decimal)%p keeps %p significant digits: %p", (value, digits, expected) => {
    expect(new Decimal(decimalTextFromDouble(value, digits)).toString()).toBe(expected);
  });

  test.each([NaN, Infinity, -Infinity, 8e28])("(decimal)%p throws OverflowException", (value) => {
    expect(() => decimalTextFromDouble(value, 15)).toThrow(
      new OverflowException("Value was either too large or too small for a Decimal."),
    );
  });
});
