import { describe, expect, test } from "bun:test";
import { arrayLength } from "#/system/array-length.ts";
import { ArgumentOutOfRangeException, OverflowException } from "#/system/exceptions/index.ts";
import { plainDate, plainDateTime, plainTime } from "#/system/temporal-helpers.ts";

/** Messages printed by .NET 10's DateOnly / TimeOnly / DateTime constructors. */
describe("date and time constructors", () => {
  test("build the Temporal values", () => {
    expect(plainDate(2024, 2, 29).toString()).toBe("2024-02-29");
    expect(plainTime(23, 59, 59, 999).toString()).toBe("23:59:59.999");
    expect(plainDateTime(2026, 1, 1).toString()).toBe("2026-01-01T00:00:00");
  });

  test("throw .NET's ArgumentOutOfRangeException", () => {
    const date = "Year, Month, and Day parameters describe an un-representable DateTime.";
    const time = "Hour, Minute, and Second parameters describe an un-representable DateTime.";

    expect(() => plainDate(2020, 13, 1)).toThrow(ArgumentOutOfRangeException);
    expect(() => plainDate(2020, 2, 30)).toThrow(date);
    expect(() => plainDate(0, 1, 1)).toThrow(date);
    expect(() => plainTime(25, 0)).toThrow(time);
    expect(() => plainDateTime(2020, 1, 1, 25, 0, 0)).toThrow(time);
    expect(() => plainTime(1, 0, 0, 1000)).toThrow(
      "Valid values are between 0 and 999, inclusive. (Parameter 'millisecond')",
    );
  });
});

describe("arrayLength", () => {
  test("passes a non-negative length and throws OverflowException for a negative one", () => {
    expect(arrayLength(3)).toBe(3);
    expect(() => arrayLength(-1)).toThrow(OverflowException);
    expect(() => arrayLength(-1)).toThrow("Arithmetic operation resulted in an overflow.");
  });
});
