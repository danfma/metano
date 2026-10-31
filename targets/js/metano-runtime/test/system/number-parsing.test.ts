import { describe, expect, test } from "bun:test";
import {
  tryParseBoolean,
  tryParseFloat64,
  tryParseInt32,
  tryParseInt64,
} from "#/system/number-parsing.ts";

describe("tryParseInt32", () => {
  test("parses a signed integer surrounded by whitespace", () => {
    expect(tryParseInt32(" -42\t")).toEqual([true, -42]);
    expect(tryParseInt32("+7")).toEqual([true, 7]);
  });

  test("accepts the Int32 bounds", () => {
    expect(tryParseInt32("2147483647")).toEqual([true, 2147483647]);
    expect(tryParseInt32("-2147483648")).toEqual([true, -2147483648]);
  });

  test("rejects values out of range", () => {
    expect(tryParseInt32("2147483648")).toEqual([false, 0]);
  });

  test("rejects fractions, separators, empty and null text", () => {
    expect(tryParseInt32("1.5")).toEqual([false, 0]);
    expect(tryParseInt32("1,000")).toEqual([false, 0]);
    expect(tryParseInt32("")).toEqual([false, 0]);
    expect(tryParseInt32(null)).toEqual([false, 0]);
  });

  test("parses negative zero as zero", () => {
    expect(Object.is(tryParseInt32("-0")[1], 0)).toBe(true);
  });
});

describe("tryParseInt64", () => {
  test("accepts values beyond Int32", () => {
    expect(tryParseInt64("2147483648")).toEqual([true, 2147483648]);
  });

  test("accepts the Int64 bounds", () => {
    expect(tryParseInt64("9223372036854775807")[0]).toBe(true);
    expect(tryParseInt64("-9223372036854775808")[0]).toBe(true);
  });

  test("rejects values beyond Int64", () => {
    expect(tryParseInt64("9223372036854775808")).toEqual([false, 0]);
  });
});

describe("tryParseFloat64", () => {
  test("parses decimals, exponents and thousands separators", () => {
    expect(tryParseFloat64("3.25")).toEqual([true, 3.25]);
    expect(tryParseFloat64("-1e3")).toEqual([true, -1000]);
    expect(tryParseFloat64("1,234.5")).toEqual([true, 1234.5]);
    expect(tryParseFloat64(".5")).toEqual([true, 0.5]);
  });

  test("parses the special values in any letter case", () => {
    expect(tryParseFloat64("NaN")[1]).toBeNaN();
    expect(tryParseFloat64("-infinity")).toEqual([true, Number.NEGATIVE_INFINITY]);
    expect(tryParseFloat64("∞")).toEqual([true, Number.POSITIVE_INFINITY]);
  });

  test("overflows to infinity", () => {
    expect(tryParseFloat64("1e400")).toEqual([true, Number.POSITIVE_INFINITY]);
  });

  test("ignores trailing NUL characters", () => {
    expect(tryParseFloat64("2.5\0")).toEqual([true, 2.5]);
  });

  test("rejects object property names", () => {
    expect(tryParseFloat64("constructor")).toEqual([false, 0]);
    expect(tryParseFloat64("__proto__")).toEqual([false, 0]);
  });

  test("rejects malformed text", () => {
    expect(tryParseFloat64("abc")).toEqual([false, 0]);
    expect(tryParseFloat64("1.2.3")).toEqual([false, 0]);
    expect(tryParseFloat64("0x10")).toEqual([false, 0]);
    expect(tryParseFloat64(" ")).toEqual([false, 0]);
  });
});

describe("tryParseBoolean", () => {
  test("parses true and false in any letter case", () => {
    expect(tryParseBoolean(" TRUE ")).toEqual([true, true]);
    expect(tryParseBoolean("False")).toEqual([true, false]);
  });

  test("rejects anything else", () => {
    expect(tryParseBoolean("yes")).toEqual([false, false]);
    expect(tryParseBoolean(null)).toEqual([false, false]);
  });
});
