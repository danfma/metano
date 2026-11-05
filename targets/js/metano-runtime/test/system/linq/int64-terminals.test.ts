import { describe, expect, test } from "bun:test";
import { InvalidOperationException, OverflowException } from "#/system/exceptions/index.ts";
import {
  averageInt64,
  averageNullableInt64,
  linq,
  maxInt64Of,
  maxNullableInt64Of,
  maxUInt64Of,
  minInt64Of,
  minNullableInt64Of,
  sumInt64,
} from "#/system/linq/index.ts";

const MAX = 9223372036854775807n;

/** Every expected value and message below was printed by the same LINQ call on .NET 10.0.12. */
describe("64-bit LINQ terminals", () => {
  test("sumInt64 is checked, skips nulls and is 0n when empty", () => {
    expect(linq([1n, 2n, 3n], sumInt64())).toBe(6n);
    expect(linq([null, 5n], sumInt64())).toBe(5n);
    expect(linq([] as bigint[], sumInt64())).toBe(0n);
    expect(linq([{ size: 4n }, { size: 6n }], sumInt64((x) => x.size))).toBe(10n);
    expect(() => linq([MAX, 1n, -5n], sumInt64())).toThrow(OverflowException);
  });

  test("averageInt64 divides the checked sum; empty throws, nullable gives null", () => {
    expect(linq([1n, 2n], averageInt64())).toBe(1.5);
    expect(() => linq([] as bigint[], averageInt64())).toThrow(
      new InvalidOperationException("Sequence contains no elements"),
    );
    expect(() => linq([MAX, MAX], averageInt64())).toThrow(OverflowException);
    expect(linq([null] as (bigint | null)[], averageNullableInt64())).toBeNull();
    expect(linq([null, 4n], averageNullableInt64())).toBe(4);
  });

  test("min and max compare bigints; empty throws, nullable skips nulls", () => {
    expect(linq([3n, -1n, 2n], minInt64Of())).toBe(-1n);
    expect(linq([3n, -1n, 2n], maxInt64Of())).toBe(3n);
    expect(linq([18446744073709551615n, 1n], maxUInt64Of())).toBe(18446744073709551615n);
    expect(() => linq([] as bigint[], minInt64Of())).toThrow(InvalidOperationException);
    expect(linq([null] as (bigint | null)[], minNullableInt64Of())).toBeNull();
    expect(linq([null, 7n, 2n], maxNullableInt64Of())).toBe(7n);
  });
});
