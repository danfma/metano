import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import { OverflowException } from "#/system/exceptions/index.ts";
import {
  bigIntegerFromDecimal,
  bigIntegerFromDouble,
  decimalTextFromBigInteger,
  doubleFromBigInteger,
  int32FromBigInteger,
  narrowFromBigInteger,
  uint32FromBigInteger,
} from "#/system/big-integer.ts";

const OVERFLOW = "Arithmetic operation resulted in an overflow.";

/** Every expected value and message below was printed by the same BigInteger cast on .NET 10.0.12. */
describe("BigInteger conversions", () => {
  test("(int) and (uint) split the generic and the per-type messages as .NET does", () => {
    expect(int32FromBigInteger(-2147483648n)).toBe(-2147483648);
    expect(() => int32FromBigInteger(2147483648n)).toThrow(new OverflowException(OVERFLOW));
    expect(() => int32FromBigInteger(4294967296n)).toThrow(
      new OverflowException("Value was either too large or too small for an Int32."),
    );
    expect(uint32FromBigInteger(4294967295n)).toBe(4294967295);
    expect(() => uint32FromBigInteger(-1n)).toThrow(new OverflowException(OVERFLOW));
    expect(() => uint32FromBigInteger(-2147483648n)).toThrow(
      new OverflowException("Value was either too large or too small for a UInt32."),
    );
  });

  test("narrower targets convert to int first, then check their range", () => {
    expect(narrowFromBigInteger(255n, 8, false)).toBe(255);
    expect(() => narrowFromBigInteger(256n, 8, false)).toThrow(new OverflowException(OVERFLOW));
    expect(() => narrowFromBigInteger(4294967296n, 16, true)).toThrow(
      new OverflowException("Value was either too large or too small for an Int32."),
    );
  });

  test("(double) truncates the significand and overflows to infinity past 1024 bits", () => {
    expect(doubleFromBigInteger(10n ** 30n)).toBe(9.999999999999999e29);
    expect(doubleFromBigInteger(-(10n ** 30n))).toBe(-9.999999999999999e29);
    expect(doubleFromBigInteger(1n << 1024n)).toBe(Number.POSITIVE_INFINITY);
    expect(doubleFromBigInteger(12345n)).toBe(12345);
  });

  test("(decimal) checks the decimal range", () => {
    expect(decimalTextFromBigInteger(79228162514264337593543950335n)).toBe(
      "79228162514264337593543950335",
    );
    expect(() => decimalTextFromBigInteger(1n << 96n)).toThrow(
      new OverflowException("Value was either too large or too small for a Decimal."),
    );
  });

  test("(BigInteger) from double and decimal truncates toward zero", () => {
    expect(bigIntegerFromDouble(1e30)).toBe(1000000000000000019884624838656n);
    expect(bigIntegerFromDouble(-0.9)).toBe(0n);
    expect(() => bigIntegerFromDouble(Number.NaN)).toThrow(
      new OverflowException("The value is not a number."),
    );
    expect(() => bigIntegerFromDouble(Number.POSITIVE_INFINITY)).toThrow(
      new OverflowException("BigInteger cannot represent infinity."),
    );
    expect(bigIntegerFromDecimal(new Decimal("-1.5"))).toBe(-1n);
  });
});
