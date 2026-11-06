import { describe, expect, test } from "bun:test";
import { hashInt64 } from "#/system/int64.ts";
import { HashCode } from "#/system/hash-code.ts";
import { hashCode, valueHashCode } from "#/system/equality.ts";

describe("HashCode", () => {
  test("same values produce same hash", () => {
    const a = HashCode.combine2(42, "hello");
    const b = HashCode.combine2(42, "hello");
    expect(a).toBe(b);
  });

  test("different values produce different hashes", () => {
    const a = HashCode.combine2(1, "a");
    const b = HashCode.combine2(2, "b");
    expect(a).not.toBe(b);
  });

  test("order matters", () => {
    const a = HashCode.combine2(1, 2);
    const b = HashCode.combine2(2, 1);
    expect(a).not.toBe(b);
  });

  test("returns number (int32 range)", () => {
    const hash = HashCode.combine3("hello", 42, true);
    expect(typeof hash).toBe("number");
    expect(Number.isInteger(hash)).toBe(true);
  });

  test("handles null and undefined", () => {
    const a = HashCode.combine(null);
    const b = HashCode.combine(undefined);
    // Both null/undefined hash to 0, so combined hashes should be equal
    expect(a).toBe(b);
  });

  test("handles booleans", () => {
    const a = HashCode.combine(true);
    const b = HashCode.combine(false);
    expect(a).not.toBe(b);
  });

  test("handles strings", () => {
    const a = HashCode.combine("hello");
    const b = HashCode.combine("world");
    expect(a).not.toBe(b);
  });

  test("handles objects with hashCode method", () => {
    const obj = { hashCode: () => 12345 };
    const hash = HashCode.combine(obj);
    // Should use the object's hashCode method
    expect(typeof hash).toBe("number");
  });

  test("accumulator pattern works", () => {
    const hc = new HashCode();
    hc.add(1);
    hc.add(2);
    hc.add(3);
    const hash = hc.toHashCode();

    expect(hash).toBe(HashCode.combine3(1, 2, 3));
  });

  test("four or more values trigger full mixing", () => {
    const hc = new HashCode();
    hc.add(1);
    hc.add(2);
    hc.add(3);
    hc.add(4);
    const hash = hc.toHashCode();

    expect(hash).toBe(HashCode.combine4(1, 2, 3, 4));
    expect(typeof hash).toBe("number");
  });

  test("deterministic across calls", () => {
    const results = Array.from({ length: 100 }, () => HashCode.combine2("test", 42));
    expect(new Set(results).size).toBe(1);
  });
});

// The values .NET 10 reports for long.GetHashCode() / ulong.GetHashCode(),
// probed with `dotnet run` (FR-046, DECIDED-13): the low and high 32-bit
// words folded with xor, so ids that differ in the high word spread.
describe("bigint hashing", () => {
  test("hashInt64 reproduces .NET's 64-bit hash codes", () => {
    expect(hashInt64(5n)).toBe(5);
    expect(hashInt64(-1n)).toBe(0);
    expect(hashInt64(1n << 32n)).toBe(1);
    expect(hashInt64(18446744073709551615n)).toBe(0);
    expect(hashInt64(-9223372036854775808n)).toBe(-2147483648);
    expect(hashInt64(1n << 40n)).toBe(256);
  });

  test("the high word spreads: values the low-word formula collided now differ", () => {
    expect(hashInt64(1n << 32n)).not.toBe(hashInt64(0n));
  });

  test("HashCode.add and the combine helpers hash a bigint through hashInt64", () => {
    expect(HashCode.combine(5n)).not.toBe(HashCode.combine(-1n));

    const hc = new HashCode();
    hc.add(5n);
    expect(hc.toHashCode()).toBe(HashCode.combine(5n));
  });

  // A bare bigint reaching hashCode / valueHashCode answers .NET's
  // long.GetHashCode() — the raw word fold (FR-040, FR-046) — while inside
  // HashCode.add the same fold enters the accumulator mix.
  test("hashCode and valueHashCode hash a bigint like long.GetHashCode", () => {
    expect(hashCode(5n)).toBe(5);
    expect(hashCode(-1n)).toBe(0);
    expect(valueHashCode(1n << 32n)).toBe(1);
    // 5 and -1 keep .NET's distinct word hashes.
    expect(hashCode(-1n)).not.toBe(hashCode(5n));
  });
});
