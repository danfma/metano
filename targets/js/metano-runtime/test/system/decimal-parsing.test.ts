import { describe, expect, test } from "bun:test";
import { parseDecimalText } from "#/system/decimal-parsing.ts";
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
