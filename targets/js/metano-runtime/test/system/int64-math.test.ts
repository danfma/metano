import { describe, expect, test } from "bun:test";
import {
  ArgumentException,
  DivideByZeroException,
  OverflowException,
} from "#/system/exceptions/index.ts";
import { INT64_MAX, INT64_MIN, UINT64_MAX } from "#/system/int64.ts";
import {
  absInt64,
  bigMulInt64,
  bigMulUInt64,
  clampInt64,
  clampUInt64,
  compareInt64,
  divRemInt64,
  divRemUInt64,
  maxInt64,
  maxUInt64,
  minInt64,
  minUInt64,
  signInt64,
} from "#/system/int64-math.ts";

/** Every expected value and message below was printed by the same call on .NET 10.0.12. */
describe("64-bit Math statics", () => {
  test("absInt64 negates negatives and rejects long.MinValue", () => {
    expect(absInt64(-5n)).toBe(5n);
    expect(absInt64(INT64_MAX)).toBe(INT64_MAX);
    expect(() => absInt64(INT64_MIN)).toThrow(
      new OverflowException("Negating the minimum value of a twos complement number is invalid."),
    );
  });

  test("max and min pick one argument, signed and unsigned", () => {
    expect(maxInt64(-1n, 1n)).toBe(1n);
    expect(minInt64(INT64_MIN, INT64_MAX)).toBe(INT64_MIN);
    expect(maxUInt64(UINT64_MAX, 1n)).toBe(UINT64_MAX);
    expect(minUInt64(UINT64_MAX, 1n)).toBe(1n);
  });

  test("signInt64 and compareInt64 return -1, 0 or 1 as numbers", () => {
    expect(signInt64(INT64_MIN)).toBe(-1);
    expect(signInt64(0n)).toBe(0);
    expect(signInt64(UINT64_MAX)).toBe(1);
    expect(compareInt64(1n, 2n)).toBe(-1);
    expect(compareInt64(2n, 2n)).toBe(0);
    expect(compareInt64(UINT64_MAX, 0n)).toBe(1);
  });

  test("clamp keeps the value inside [min, max] and rejects min > max", () => {
    expect(clampInt64(-20n, -10n, -1n)).toBe(-10n);
    expect(clampInt64(5n, 1n, 10n)).toBe(5n);
    expect(clampUInt64(UINT64_MAX, 1n, 10n)).toBe(10n);
    expect(() => clampInt64(5n, -1n, -10n)).toThrow(
      new ArgumentException("'-1' cannot be greater than -10."),
    );
    expect(() => clampUInt64(5n, 10n, 1n)).toThrow(new ArgumentException("'10' cannot be greater than 1."));
  });

  test("divRem returns the truncated quotient and the remainder, with .NET's exceptions", () => {
    expect(divRemInt64(-7n, 2n)).toEqual([-3n, -1n]);
    expect(divRemUInt64(UINT64_MAX, 10n)).toEqual([1844674407370955161n, 5n]);
    expect(() => divRemInt64(7n, 0n)).toThrow(DivideByZeroException);
    expect(() => divRemUInt64(7n, 0n)).toThrow(DivideByZeroException);
    expect(() => divRemInt64(INT64_MIN, -1n)).toThrow(OverflowException);
  });

  test("bigMul returns the high and low halves of the 128-bit product", () => {
    expect(bigMulInt64(INT64_MAX, INT64_MAX)).toEqual([4611686018427387903n, 1n]);
    expect(bigMulInt64(INT64_MIN, INT64_MIN)).toEqual([4611686018427387904n, 0n]);
    expect(bigMulInt64(-1n, 1n)).toEqual([-1n, -1n]);
    expect(bigMulUInt64(UINT64_MAX, UINT64_MAX)).toEqual([18446744073709551614n, 1n]);
  });
});
