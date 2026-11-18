import { describe, expect, test } from "bun:test";
import { getNumberType } from "#/system/reflection/get-number-type.ts";
import { DoubleType, Int32Type } from "#/system/reflection/primitive-types.ts";

describe("getNumberType", () => {
  test.each([0, 1, -1, 2147483647, -2147483648])("%d is Int32", (n) => {
    expect(getNumberType(n)).toBe(Int32Type);
  });

  test.each([2147483648, -2147483649, 1.5, Number.NaN, Infinity, 2 ** 53])("%d is Double", (n) => {
    expect(getNumberType(n)).toBe(DoubleType);
  });
});
