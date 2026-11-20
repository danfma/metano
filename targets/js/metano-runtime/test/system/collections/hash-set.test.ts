import {
  describe,
  expect,
  test,
} from "bun:test";

import { HashCode } from "#/system/hash-code.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { Decimal } from "decimal.js";
import { Temporal } from "@js-temporal/polyfill";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

// Mock value object with equals/hashCode
class UserId {
  constructor(readonly value: string) {}

  equals(other: unknown): boolean {
    return other instanceof UserId && this.value === other.value;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    return hc.toHashCode();
  }
}


class Tag {
  constructor(readonly name: string) {}

  equals(other: unknown): boolean {
    return other instanceof Tag && other.name === this.name;
  }
}

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("HashSet", () => {
  // ─── Primitives ────────────────────────────────────────

  test("add/contains/remove with numbers", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(2);
    set.add(3);
    expect(set.count).toBe(3);
    expect(set.contains(2)).toBe(true);
    expect(set.contains(4)).toBe(false);
    set.remove(2);
    expect(set.count).toBe(2);
    expect(set.contains(2)).toBe(false);
  });

  test("add/contains with strings", () => {
    const set = new HashSet<string>();
    set.add("a");
    set.add("b");
    expect(set.count).toBe(2);
    expect(set.contains("a")).toBe(true);
    expect(set.contains("c")).toBe(false);
  });

  test("deduplicates primitives", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(1);
    set.add(1);
    expect(set.count).toBe(1);
  });

  // ─── Value objects ─────────────────────────────────────

  test("deduplicates by equals/hashCode", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    set.add(new UserId("abc")); // same value, different reference
    set.add(new UserId("def"));
    expect(set.count).toBe(2);
  });

  test("contains() uses equals for lookup", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    expect(set.contains(new UserId("abc"))).toBe(true); // different ref, same value
    expect(set.contains(new UserId("xyz"))).toBe(false);
  });

  test("remove() uses equals for lookup", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("abc"));
    expect(set.remove(new UserId("abc"))).toBe(true); // different ref
    expect(set.count).toBe(0);
    expect(set.remove(new UserId("abc"))).toBe(false); // already gone
  });

  // ─── Iteration ─────────────────────────────────────────

  test("iterates all items", () => {
    const set = new HashSet<number>();
    set.add(1);
    set.add(2);
    set.add(3);
    const items = [...set];
    expect(items.sort()).toEqual([1, 2, 3]);
  });

  test("iterates value objects", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("a"));
    set.add(new UserId("b"));
    const values = [...set].map((u) => u.value).sort();
    expect(values).toEqual(["a", "b"]);
  });

  test("for..of iterates all items", () => {
    const set = new HashSet<number>();
    set.add(10);
    set.add(20);
    const result: number[] = [];

    for (const item of set) {
      result.push(item);
    }

    expect(result.sort()).toEqual([10, 20]);
  });

  test("Symbol.iterator returns iterator", () => {
    const set = new HashSet<string>();
    set.add("x");
    set.add("y");
    const iter = set[Symbol.iterator]();
    const items: string[] = [];

    for (const item of iter) {
      items.push(item);
    }

    expect(items.sort()).toEqual(["x", "y"]);
  });

  // ─── Other operations ──────────────────────────────────

  test("clear() removes all items", () => {
    const set = new HashSet<UserId>();
    set.add(new UserId("a"));
    set.add(new UserId("b"));
    set.clear();
    expect(set.count).toBe(0);
    expect(set.contains(new UserId("a"))).toBe(false);
  });

  test("constructor with iterable", () => {
    const set = new HashSet([1, 2, 3, 2, 1]);
    expect(set.count).toBe(3);
  });

  test("toArray()", () => {
    const set = new HashSet<number>();
    set.add(5);
    set.add(10);
    expect(set.toArray().sort((a, b) => a - b)).toEqual([5, 10]);
  });

  test("mixed primitives and iteration", () => {
    const set = new HashSet<number | string>();
    set.add(1);
    set.add("one");
    set.add(1);
    set.add("one");
    expect(set.count).toBe(2);
  });
});

describe("HashSet (spec 011)", () => {
  test("does not expose the 2.x aliases", () => {
    const set = HashSet.of([1]);

    for (const alias of ["size", "has", "delete", "values", "forEach"]) {
      expect(alias in set).toBe(false);
    }
  });

  test("add returns whether it added; count, contains, remove", () => {
    const set = new HashSet<number>();

    expect(set.add(1)).toBe(true);
    expect(set.add(1)).toBe(false);
    expect(set.count).toBe(1);
    expect(set.contains(1)).toBe(true);
    expect(set.remove(1)).toBe(true);
    expect(set.remove(1)).toBe(false);
  });

  test("insertion order across primitives and objects", () => {
    const a = {};
    const set = HashSet.of<unknown>([1, a, "x", 2]);

    expect([...set]).toEqual([1, a, "x", 2]);
  });

  test("Decimal and Temporal values key by value (equals without hashCode)", () => {
    const decimals = HashSet.of([new Decimal(1)]);

    expect(decimals.contains(new Decimal("1.0"))).toBe(true);

    const dates = new Dictionary<Temporal.PlainDate, string>();

    dates.set(Temporal.PlainDate.from("2024-01-02"), "a");

    expect(dates.get(Temporal.PlainDate.from("2024-01-02"))).toBe("a");
  });

  test("a value with equals but no hashCode keys by equals", () => {
    const set = HashSet.of([new Tag("a")]);

    expect(set.contains(new Tag("a"))).toBe(true);
    expect(set.add(new Tag("a"))).toBe(false);
    expect(set.add(new Tag("b"))).toBe(true);
  });

  test("set algebra", () => {
    const set = HashSet.of([1, 2, 3]);

    expect(set.isSubsetOf([1, 2, 3, 4])).toBe(true);
    expect(set.isProperSubsetOf([1, 2, 3])).toBe(false);
    expect(set.isSupersetOf([1, 1])).toBe(true);
    expect(set.isProperSupersetOf([1, 2])).toBe(true);
    expect(set.overlaps([9, 3])).toBe(true);
    expect(set.setEquals([3, 2, 1, 1])).toBe(true);

    const mutable = HashSet.of([1, 2, 3]);

    mutable.unionWith([4]);
    mutable.exceptWith([1]);
    mutable.intersectWith([2, 3, 4, 5]);
    mutable.symmetricExceptWith([4, 6]);

    expect([...mutable]).toEqual([2, 3, 6]);

    expect(thrown(() => set.overlaps(null as never)).message).toBe(
      "Value cannot be null. (Parameter 'other')",
    );
  });

  test("removeWhere, tryGetValue, copyTo, JSON, toString, withCapacity", () => {
    const set = HashSet.of([1, 2, 3, 4], null, [Int32Type]);
    const stored = new Tag("x");

    expect(set.removeWhere((x) => x % 2 === 0)).toBe(2);
    expect(HashSet.of([stored]).tryGetValue(new Tag("x"), null)).toEqual([true, stored]);
    expect(set.tryGetValue(9, 0)).toEqual([false, 0]);
    expect(JSON.stringify(set)).toBe("[1,3]");
    expect(set.toString()).toBe("System.Collections.Generic.HashSet`1[System.Int32]");
    expect(HashSet.withCapacity<number>(3).initAdd([1, 1]).count).toBe(1);

    expect(thrown(() => HashSet.from(null as never)).message).toBe(
      "Value cannot be null. (Parameter 'collection')",
    );
  });

  test("only an addition invalidates a running enumeration", () => {
    const adding = HashSet.of([1, 2]);

    expect(thrown(() => {
      for (const _ of adding) adding.add(9);
    }).message).toBe("Collection was modified; enumeration operation may not execute.");

    for (const mutate of [
      (s: HashSet<number>, x: number) => s.remove(x),
      (s: HashSet<number>) => s.clear(),
      (s: HashSet<number>) => s.add(1),
      (s: HashSet<number>) => s.exceptWith([1]),
      (s: HashSet<number>) => s.intersectWith([1]),
      (s: HashSet<number>) => s.removeWhere(() => true),
    ]) {
      const set = HashSet.of([1, 2]);

      expect(() => {
        for (const x of set) mutate(set, x);
      }).not.toThrow();
    }
  });

  test("storage is invisible to keys and spread", () => {
    expect(Object.keys(HashSet.of([1]))).toEqual([]);
    expect({ ...HashSet.of([1]) }).toEqual({});
  });
});
