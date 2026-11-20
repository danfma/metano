import { describe, expect, test } from "bun:test";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

// Same-instance results read from .NET 10 (2026-11-19).

const ignoreCase = {
  equals: (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value: string): number => value.toLowerCase().length,
};

describe("ImmutableHashSet", () => {
  test("set operations and same-instance results", () => {
    const set = ImmutableHashSet.of([1, 2]);

    expect(set.add(1)).toBe(set);
    expect(set.remove(9)).toBe(set);
    expect(set.union([1])).toBe(set);
    expect(set.except([9])).toBe(set);
    expect(set.intersect([1, 2, 3])).not.toBe(set);
    expect(set.symmetricExcept([])).not.toBe(set);
    expect(set.clear()).toBe(ImmutableHashSet.empty());
    expect(set.withComparer(null)).toBe(set);
    expect(ImmutableHashSet.from(set)).toBe(set);
    expect([...set.union([3, 1])]).toEqual([1, 2, 3]);
    expect([...set.intersect([2, 9])]).toEqual([2]);
    expect([...set.except([1])]).toEqual([2]);
    expect([...set.symmetricExcept([2, 3])]).toEqual([1, 3]);
    expect(set.setEquals([2, 1, 1])).toBe(true);
    expect(set.isProperSubsetOf([1, 2, 3])).toBe(true);
  });

  test("the comparer decides equality; tryGetValue returns the stored element", () => {
    const set = ImmutableHashSet.of(["Abc"], ignoreCase);

    expect(set.contains("ABC")).toBe(true);
    expect(set.tryGetValue("ABC", "")).toEqual([true, "Abc"]);
    expect(set.tryGetValue("x", "")).toEqual([false, "x"]);
    expect(set.tryGetValue("y", null)).toEqual([false, "y"]);
    expect(set.keyComparer).toBe(ignoreCase);
    expect(set.withComparer(null).count).toBe(1);
  });

  test("null elements are allowed; a null other throws", () => {
    expect(ImmutableHashSet.of<string | null>(["a", null]).count).toBe(2);
    expect(() => ImmutableHashSet.of([1]).union(null as never)).toThrow("(Parameter 'other')");
  });

  test("toJSON and toString", () => {
    const set = ImmutableHashSet.of([1], null, [Int32Type]);

    expect(JSON.stringify(set)).toBe("[1]");
    expect(String(set)).toBe("System.Collections.Immutable.ImmutableHashSet`1[System.Int32]");
  });
});
