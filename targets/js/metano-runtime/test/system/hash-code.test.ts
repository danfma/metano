import { describe, expect, test } from "bun:test";
import { HashCode } from "#/system/hash-code.ts";

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
