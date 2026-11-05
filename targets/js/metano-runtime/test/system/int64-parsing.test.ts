import { describe, expect, test } from "bun:test";
import {
  ArgumentNullException,
  FormatException,
  NotSupportedException,
  OverflowException,
} from "#/system/exceptions/index.ts";
import {
  parseInt64,
  parseUInt64,
  tryParseInt64Value,
  tryParseUInt64,
} from "#/system/int64-parsing.ts";

const INTEGER = 7;
const HEX_NUMBER = 515;
const INTEGER_THOUSANDS = 71;
const NUMBER = 111;
const INT64_RANGE = "Value was either too large or too small for an Int64.";
const UINT64_RANGE = "Value was either too large or too small for a UInt64.";

/** Every expected value and message below was printed by `long.Parse` / `ulong.Parse` on .NET 10.0.12. */
describe("parseInt64 and parseUInt64", () => {
  test("Integer accepts .NET's white space, one leading sign and trailing NULs", () => {
    expect(parseInt64(" 12 ")).toBe(12n);
    expect(parseInt64("\t-12\n")).toBe(-12n);
    expect(parseInt64("+5")).toBe(5n);
    expect(parseInt64("12\0\0")).toBe(12n);
    expect(parseInt64("12 \0")).toBe(12n);
    expect(parseInt64("-9223372036854775808")).toBe(-9223372036854775808n);
    expect(parseUInt64("18446744073709551615")).toBe(18446744073709551615n);
    expect(parseUInt64("-0")).toBe(0n);
  });

  test("Integer rejects other text with FormatException", () => {
    for (const text of ["", " ", " 12", "--5", "- 5", "1,234", "0x10", "1e3", "\u000012", "5-", "١٢"]) {
      expect(() => parseInt64(text)).toThrow(
        new FormatException(`The input string '${text}' was not in a correct format.`),
      );
    }
  });

  test("out-of-range values throw OverflowException with the type's message", () => {
    expect(() => parseInt64("9223372036854775808")).toThrow(new OverflowException(INT64_RANGE));
    expect(() => parseUInt64("-5")).toThrow(new OverflowException(UINT64_RANGE));
    expect(() => parseUInt64("18446744073709551616")).toThrow(new OverflowException(UINT64_RANGE));
  });

  test("null throws ArgumentNullException", () => {
    expect(() => parseInt64(null)).toThrow(new ArgumentNullException("s"));
  });

  test("HexNumber reads up to 16 significant digits as two's complement", () => {
    expect(parseInt64("FFFFFFFFFFFFFFFF", HEX_NUMBER)).toBe(-1n);
    expect(parseUInt64("FFFFFFFFFFFFFFFF", HEX_NUMBER)).toBe(18446744073709551615n);
    expect(parseInt64("8000000000000000", HEX_NUMBER)).toBe(-9223372036854775808n);
    expect(parseInt64(" 0000000000000000000ff ", HEX_NUMBER)).toBe(255n);
    expect(() => parseInt64("10000000000000000", HEX_NUMBER)).toThrow(new OverflowException(INT64_RANGE));
    expect(() => parseInt64("-FF", HEX_NUMBER)).toThrow(FormatException);
    expect(() => parseInt64("0x10", HEX_NUMBER)).toThrow(FormatException);
  });

  test("AllowThousands accepts group separators after the first digit", () => {
    expect(parseInt64("1,234", INTEGER_THOUSANDS)).toBe(1234n);
    expect(parseInt64("1,,2", INTEGER_THOUSANDS)).toBe(12n);
    expect(parseInt64("1,", INTEGER_THOUSANDS)).toBe(1n);
    expect(() => parseInt64(",1", INTEGER_THOUSANDS)).toThrow(FormatException);
  });

  test("Number adds a trailing sign and a fraction of zeros", () => {
    expect(parseInt64("-1,234.000", NUMBER)).toBe(-1234n);
    expect(parseInt64("5 -", NUMBER)).toBe(-5n);
    expect(parseInt64("1.", NUMBER)).toBe(1n);
    expect(parseInt64(".0", NUMBER)).toBe(0n);
    expect(parseUInt64("0,-", NUMBER)).toBe(0n);
    expect(() => parseInt64("1.5", NUMBER)).toThrow(new OverflowException(INT64_RANGE));
    expect(() => parseUInt64("-0.0", NUMBER)).toThrow(new OverflowException(UINT64_RANGE));
    expect(() => parseInt64("-5-", NUMBER)).toThrow(FormatException);
    expect(() => parseInt64(".", NUMBER)).toThrow(FormatException);
  });

  test("other styles throw NotSupportedException", () => {
    expect(() => parseInt64("1", 167)).toThrow(NotSupportedException);
    expect(() => tryParseInt64Value("1", 167)).toThrow(NotSupportedException);
  });
});

describe("tryParseInt64Value and tryParseUInt64", () => {
  test("return [true, value] or [false, 0n] where Parse would throw", () => {
    expect(tryParseInt64Value(" 42 ")).toEqual([true, 42n]);
    expect(tryParseInt64Value(null)).toEqual([false, 0n]);
    expect(tryParseInt64Value("x")).toEqual([false, 0n]);
    expect(tryParseInt64Value("9223372036854775808")).toEqual([false, 0n]);
    expect(tryParseUInt64("ff", HEX_NUMBER)).toEqual([true, 255n]);
    expect(tryParseUInt64("-1", INTEGER)).toEqual([false, 0n]);
  });
});
