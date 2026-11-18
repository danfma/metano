import { describe, expect, test } from "bun:test";
import { Decimal } from "#/system/decimal.ts";
import {
  bigintFromDynamic,
  charFromDynamic,
  decimalSourceFromDynamic,
  numberFromDynamic,
} from "#/system/dynamic-conversion.ts";
import { InvalidCastException, OverflowException } from "#/system/exceptions/index.ts";

/** The expected values are the ones .NET 10 gives for the same `(T)d` casts. */
describe("numberFromDynamic", () => {
  test("converts an integer held in a number as an integer", () => {
    expect(numberFromDynamic(7, "Int32")).toBe(7);
    expect(numberFromDynamic(300, "Byte")).toBe(44);
    expect(numberFromDynamic(-1, "UInt32")).toBe(4294967295);
    expect(numberFromDynamic(40000, "Int16")).toBe(-25536);
  });

  test("converts any other number as a double: truncated and saturated", () => {
    expect(numberFromDynamic(3.7, "Int32")).toBe(3);
    expect(numberFromDynamic(-3.7, "Int32")).toBe(-3);
    expect(numberFromDynamic(1e20, "Int32")).toBe(2147483647);
    expect(numberFromDynamic(300.5, "Byte")).toBe(255);
    expect(numberFromDynamic(Number.NaN, "Int32")).toBe(0);
  });

  test("range-checks in a checked context", () => {
    expect(numberFromDynamic(255, "Byte", true)).toBe(255);
    expect(() => numberFromDynamic(300, "Byte", true)).toThrow(OverflowException);
    expect(() => numberFromDynamic(Number.NaN, "Int32", true)).toThrow(OverflowException);
  });

  test("converts a bigint as a 64-bit integer", () => {
    expect(numberFromDynamic(10n, "Int32")).toBe(10);
    expect(numberFromDynamic(4294967297n, "Int32")).toBe(1);
    expect(() => numberFromDynamic(4294967297n, "Int32", true)).toThrow(OverflowException);
    expect(numberFromDynamic(10n, "Double")).toBe(10);
  });

  test("converts a decimal, throwing out of range in every context", () => {
    expect(numberFromDynamic(new Decimal("3.9"), "Int32")).toBe(3);
    expect(numberFromDynamic(new Decimal("2.5"), "Double")).toBe(2.5);
    expect(() => numberFromDynamic(new Decimal("1e12"), "Int32")).toThrow(OverflowException);
  });

  test("keeps a double and rounds a float", () => {
    expect(numberFromDynamic(0.1, "Double")).toBe(0.1);
    expect(numberFromDynamic(0.1, "Single")).toBe(Math.fround(0.1));
  });

  test("rejects null and non-numeric values", () => {
    expect(() => numberFromDynamic(null, "Int32")).toThrow(
      "Cannot convert null to 'int' because it is a non-nullable value type",
    );
    expect(() => numberFromDynamic("abc", "Int32")).toThrow(InvalidCastException);
    expect(() => numberFromDynamic("42", "Int32")).toThrow("Cannot convert type 'string' to 'int'");
    expect(() => numberFromDynamic(true, "Int32")).toThrow("Cannot convert type 'bool' to 'int'");
    expect(() => numberFromDynamic({}, "Double")).toThrow(InvalidCastException);
  });
});

describe("charFromDynamic", () => {
  test("keeps a char and converts a number to its code", () => {
    expect(charFromDynamic("a")).toBe("a");
    expect(charFromDynamic(97)).toBe("a");
    expect(charFromDynamic(65601)).toBe("A");
  });

  test("rejects a longer string and an out-of-range code when checked", () => {
    expect(() => charFromDynamic("ab")).toThrow(InvalidCastException);
    expect(() => charFromDynamic(65601, true)).toThrow(OverflowException);
  });
});

describe("bigintFromDynamic", () => {
  test("converts numbers, bigints and decimals to long", () => {
    expect(bigintFromDynamic(5, "Int64")).toBe(5n);
    expect(bigintFromDynamic(3.7, "Int64")).toBe(3n);
    expect(bigintFromDynamic(-1, "UInt64")).toBe(18446744073709551615n);
    expect(bigintFromDynamic(10n, "Int64")).toBe(10n);
    expect(bigintFromDynamic(new Decimal("12.9"), "Int64")).toBe(12n);
  });

  test("range-checks a ulong in a checked context", () => {
    expect(() => bigintFromDynamic(-1n, "UInt64", true)).toThrow(OverflowException);
  });

  test("converts to BigInteger exactly, overflowing on NaN", () => {
    expect(bigintFromDynamic(2n ** 70n, "BigInteger")).toBe(2n ** 70n);
    expect(bigintFromDynamic(-2.5, "BigInteger")).toBe(-2n);
    expect(() => bigintFromDynamic(Number.NaN, "BigInteger")).toThrow(OverflowException);
  });

  test("rejects null and non-numeric values", () => {
    expect(() => bigintFromDynamic(null, "Int64")).toThrow(InvalidCastException);
    expect(() => bigintFromDynamic("1", "Int64")).toThrow(InvalidCastException);
  });
});

describe("decimalSourceFromDynamic", () => {
  test("gives new Decimal the exact value of every numeric value", () => {
    expect(new Decimal(decimalSourceFromDynamic(2.5)).toString()).toBe("2.5");
    expect(decimalSourceFromDynamic(12345678901234567890n)).toBe("12345678901234567890");
    expect(decimalSourceFromDynamic(new Decimal("1.2500000000000000000000000001"))).toBe(
      "1.2500000000000000000000000001",
    );
  });

  test("overflows on NaN, infinity and a bigint beyond 2^96", () => {
    expect(() => decimalSourceFromDynamic(Number.NaN)).toThrow(OverflowException);
    expect(() => decimalSourceFromDynamic(Number.POSITIVE_INFINITY)).toThrow(OverflowException);
    expect(() => decimalSourceFromDynamic(2n ** 96n)).toThrow(OverflowException);
  });

  test("rejects null and non-numeric values", () => {
    expect(() => decimalSourceFromDynamic(null)).toThrow(InvalidCastException);
    expect(() => decimalSourceFromDynamic("1")).toThrow(InvalidCastException);
  });
});
