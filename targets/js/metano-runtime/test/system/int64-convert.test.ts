import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import {
  ArgumentException,
  ArgumentOutOfRangeException,
  FormatException,
  InvalidCastException,
  OverflowException,
} from "#/system/exceptions/index.ts";
import {
  convertDecimalToInt64,
  convertDecimalToUInt64,
  convertDoubleToInt64,
  convertDoubleToUInt64,
  convertNarrow,
  convertObjectToInt64,
  convertStringToInt64,
  convertStringToUInt64,
} from "#/system/int64-convert.ts";

const OVERFLOW = "Arithmetic operation resulted in an overflow.";

/** Every expected value and message below was printed by `System.Convert` on .NET 10.0.12. */
describe("Convert into 64-bit integers", () => {
  test("a double rounds half to even, then checks the range", () => {
    expect(convertDoubleToInt64(2.5)).toBe(2n);
    expect(convertDoubleToInt64(3.5)).toBe(4n);
    expect(convertDoubleToInt64(-2.5)).toBe(-2n);
    expect(convertDoubleToInt64(-0.5)).toBe(0n);
    expect(convertDoubleToInt64(-9223372036854775808)).toBe(-9223372036854775808n);
    expect(convertDoubleToUInt64(-0.5)).toBe(0n);
    expect(convertDoubleToUInt64(9223372036854775808)).toBe(9223372036854775808n);
    expect(() => convertDoubleToInt64(9223372036854775808)).toThrow(new OverflowException(OVERFLOW));
    expect(() => convertDoubleToInt64(Number.NaN)).toThrow(new OverflowException(OVERFLOW));
    expect(() => convertDoubleToUInt64(-0.6)).toThrow(new OverflowException(OVERFLOW));
  });

  test("a decimal rounds half to even, then checks the range with the type's message", () => {
    expect(convertDecimalToInt64(new Decimal("2.5"))).toBe(2n);
    expect(convertDecimalToInt64(new Decimal("-0.6"))).toBe(-1n);
    expect(convertDecimalToUInt64(new Decimal("9223372036854775807.5"))).toBe(9223372036854775808n);
    expect(() => convertDecimalToInt64(new Decimal("9223372036854775807.5"))).toThrow(
      new OverflowException("Value was either too large or too small for an Int64."),
    );
    expect(() => convertDecimalToUInt64(new Decimal("-0.6"))).toThrow(
      new OverflowException("Value was either too large or too small for a UInt64."),
    );
  });

  test("a string without a base parses as long.Parse; null is zero", () => {
    expect(convertStringToInt64(null)).toBe(0n);
    expect(convertStringToInt64(" 12 ")).toBe(12n);
    expect(() => convertStringToInt64("")).toThrow(FormatException);
    expect(convertStringToUInt64(null)).toBe(0n);
  });

  test("a string with a base follows .NET's digit grammar", () => {
    expect(convertStringToInt64("FFFFFFFFFFFFFFFF", 16)).toBe(-1n);
    expect(convertStringToUInt64("FFFFFFFFFFFFFFFF", 16)).toBe(18446744073709551615n);
    expect(convertStringToInt64("0x10", 16)).toBe(16n);
    expect(convertStringToInt64("1777777777777777777777", 8)).toBe(-1n);
    expect(convertStringToInt64("101", 2)).toBe(5n);
    expect(convertStringToInt64("+5", 16)).toBe(5n);
    expect(convertStringToInt64("-9223372036854775808", 10)).toBe(-9223372036854775808n);
    expect(convertStringToInt64(null, 16)).toBe(0n);
    expect(() => convertStringToInt64("1", 3)).toThrow(new ArgumentException("Invalid Base."));
    expect(() => convertStringToInt64(null, 3)).toThrow(new ArgumentException("Invalid Base."));
    expect(() => convertStringToInt64("-1", 16)).toThrow(
      new ArgumentException("String cannot contain a minus sign if the base is not 10."),
    );
    expect(() => convertStringToUInt64("-1", 10)).toThrow(
      new OverflowException(
        "The string was being parsed as an unsigned number and could not have a negative sign.",
      ),
    );
    expect(() => convertStringToInt64(" 10", 10)).toThrow(
      new FormatException("Could not find any recognizable digits."),
    );
    expect(() => convertStringToInt64("10 ", 2)).toThrow(
      new FormatException("Additional non-parsable characters are at the end of the string."),
    );
    expect(() => convertStringToInt64("2000000000000000000000", 8)).toThrow(
      new OverflowException("Value was either too large or too small for a UInt64."),
    );
    expect(() => convertStringToInt64("9223372036854775808", 10)).toThrow(
      new OverflowException("Value was either too large or too small for an Int64."),
    );
    expect(() => convertStringToInt64("", 10)).toThrow(ArgumentOutOfRangeException);
  });

  test("an object dispatches on its runtime value", () => {
    expect(convertObjectToInt64(null)).toBe(0n);
    expect(convertObjectToInt64(5)).toBe(5n);
    expect(convertObjectToInt64(2.5)).toBe(2n);
    expect(convertObjectToInt64("12")).toBe(12n);
    expect(convertObjectToInt64(true)).toBe(1n);
    expect(convertObjectToInt64(new Decimal("3.5"))).toBe(4n);
    expect(() => convertObjectToInt64(18446744073709551615n)).toThrow(OverflowException);
    expect(() => convertObjectToInt64({})).toThrow(InvalidCastException);
  });
});

describe("convertNarrow", () => {
  const messages: [Parameters<typeof convertNarrow>[1], bigint, string][] = [
    ["Int32", 9223372036854775807n, "an Int32"],
    ["UInt32", -1n, "a UInt32"],
    ["Int16", 9223372036854775807n, "an Int16"],
    ["UInt16", -1n, "a UInt16"],
    ["Byte", -1n, "an unsigned byte"],
    ["SByte", 9223372036854775807n, "a signed byte"],
    ["Char", -1n, "a character"],
    ["Int64", 18446744073709551615n, "an Int64"],
    ["UInt64", -1n, "a UInt64"],
  ];

  test.each(messages)("to %s throws .NET's message outside the range", (target, value, noun) => {
    expect(() => convertNarrow(value, target)).toThrow(
      new OverflowException(`Value was either too large or too small for ${noun}.`),
    );
  });

  test("returns the value inside the range", () => {
    expect(convertNarrow(65n, "Char")).toBe(65n);
    expect(convertNarrow(-2147483648n, "Int32")).toBe(-2147483648n);
  });
});
