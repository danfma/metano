import { describe, expect, test } from "bun:test";
import { ConcurrentDictionary } from "#/system/collections/concurrent-dictionary.ts";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { ReadOnlyList } from "#/system/collections/read-only-list.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

// Messages and behaviours read from .NET 10 (2026-11-19).

class Point {
  constructor(
    readonly x: number,
    readonly y: number,
  ) {}

  equals(other: unknown): boolean {
    return other instanceof Point && other.x === this.x && other.y === this.y;
  }

  hashCode(): number {
    return this.x * 31 + this.y;
  }
}

describe("ConcurrentDictionary", () => {
  test("is a sibling of Dictionary with the dictionary surface", () => {
    const dictionary = ConcurrentDictionary.assign([
      ["a", 1],
      ["a", 2],
      ["b", 3],
    ]);

    expect(dictionary).not.toBeInstanceOf(Dictionary);

    expect([...dictionary]).toEqual([
      ["a", 2],
      ["b", 3],
    ]);

    expect(dictionary.get("a")).toBe(2);
    expect(dictionary.count).toBe(2);
    expect(dictionary.isEmpty).toBe(false);
    expect(dictionary.containsKey("b")).toBe(true);
    expect(dictionary.tryGetValue("z", 0)).toEqual([false, 0]);
    expect(dictionary.getValueOrDefault("z", -1)).toBe(-1);
    expect(dictionary.update("a", (v) => v + 1)).toBe(3);

    expect(() => dictionary.get("z")).toThrow(
      "The given key 'z' was not present in the dictionary.",
    );
  });

  test("keys by elementEquals / elementHashCode", () => {
    const dictionary = new ConcurrentDictionary<Point, string>();

    dictionary.set(new Point(1, 2), "a");

    expect(dictionary.get(new Point(1, 2))).toBe("a");
    expect(dictionary.tryAdd(new Point(1, 2), "b")).toBe(false);
  });

  test("tryAdd, tryUpdate and tryRemove", () => {
    const dictionary = new ConcurrentDictionary<number, number>();

    expect(dictionary.tryAdd(1, 1)).toBe(true);
    expect(dictionary.tryAdd(1, 2)).toBe(false);
    expect(dictionary.tryUpdate(1, 5, 2)).toBe(false);
    expect(dictionary.tryUpdate(1, 5, 1)).toBe(true);
    expect(dictionary.get(1)).toBe(5);
    expect(dictionary.tryUpdate(9, 1, 1)).toBe(false);
    expect(dictionary.tryRemove(1, 0)).toEqual([true, 5]);
    expect(dictionary.tryRemove(1, 0)).toEqual([false, 0]);
  });

  test("getOrAdd inserts once; a value the factory stored itself wins", () => {
    const dictionary = new ConcurrentDictionary<string, number>();
    let calls = 0;

    expect(dictionary.getOrAdd("a", 1)).toBe(1);
    expect(dictionary.getOrAdd("a", 2)).toBe(1);

    expect(
      dictionary.getOrAddWith("a", () => {
        calls++;

        return 3;
      }),
    ).toBe(1);

    expect(calls).toBe(0);

    expect(
      dictionary.getOrAddWith("b", (key) => {
        dictionary.set(key, 7);

        return 9;
      }),
    ).toBe(7);

    expect(dictionary.getOrAddWith("c", (_key, argument: number) => argument * 2, 21)).toBe(42);
  });

  test("a function-typed value is a value, never a factory", () => {
    const dictionary = new ConcurrentDictionary<string, () => number>();
    const value = () => 1;

    expect(dictionary.getOrAdd("a", value)).toBe(value);
  });

  test("addOrUpdate adds, then updates", () => {
    const dictionary = new ConcurrentDictionary<number, number>();

    expect(dictionary.addOrUpdate(1, 1, (_k, v) => v + 10)).toBe(1);
    expect(dictionary.addOrUpdate(1, 1, (_k, v) => v + 10)).toBe(11);

    expect(
      dictionary.addOrUpdateWith(
        2,
        (_k, a: number) => a,
        (_k, v, a) => v + a,
        5,
      ),
    ).toBe(5);

    expect(
      dictionary.addOrUpdateWith(
        2,
        (_k, a: number) => a,
        (_k, v, a) => v + a,
        5,
      ),
    ).toBe(10);
  });

  test("addOrUpdate retries when its factory changes the key, as .NET's loop", () => {
    const dictionary = new ConcurrentDictionary<number, number>();
    let first = true;

    dictionary.set(1, 1);

    const result = dictionary.addOrUpdate(1, 0, (_k, v) => {
      if (first) {
        first = false;
        dictionary.set(1, 100);
      }

      return v + 1;
    });

    expect(result).toBe(101);
    expect(dictionary.get(1)).toBe(101);
  });

  test("enumeration never throws while the dictionary changes", () => {
    const dictionary = ConcurrentDictionary.assign([
      [1, 1],
      [2, 2],
    ]);

    const seen: number[] = [];

    for (const [key] of dictionary) {
      seen.push(key);
      dictionary.tryRemove(2, 0);
      dictionary.set(3, 3);
    }

    expect(seen).toEqual([1, 3]);
    expect(dictionary.count).toBe(2);
  });

  test("keys and values are ReadOnlyList snapshots", () => {
    const dictionary = ConcurrentDictionary.assign([[1, 10]]);
    const keys = dictionary.keys;

    dictionary.set(2, 20);

    expect(keys).toBeInstanceOf(ReadOnlyList);
    expect(keys.count).toBe(1);
    expect([...dictionary.values]).toEqual([10, 20]);
    expect(() => keys.add(3)).toThrow("Collection is read-only.");

    expect(dictionary.toArray()).toEqual([
      [1, 10],
      [2, 20],
    ]);
  });

  test("arguments are checked as .NET", () => {
    const dictionary = new ConcurrentDictionary<string | null, number>();

    for (const action of [
      () => dictionary.get(null),
      () => dictionary.tryAdd(null, 1),
      () => dictionary.remove(null),
      () => dictionary.getOrAdd(null, 1),
    ]) {
      expect(action).toThrow("Value cannot be null. (Parameter 'key')");
    }

    expect(() => dictionary.getOrAddWith("a", null as never)).toThrow("(Parameter 'valueFactory')");

    expect(() => dictionary.addOrUpdateWith("a", null as never, (_k, v) => v)).toThrow(
      "(Parameter 'addValueFactory')",
    );

    expect(() => dictionary.addOrUpdate("a", 1, null as never)).toThrow(
      "(Parameter 'updateValueFactory')",
    );

    expect(() => dictionary.add("x", 1)).not.toThrow();
    expect(() => dictionary.add("x", 2)).toThrow("The key already existed in the dictionary.");

    expect(() =>
      ConcurrentDictionary.from([
        [1, 1],
        [1, 2],
      ]),
    ).toThrow("The source argument contains duplicate keys.");

    expect(() => ConcurrentDictionary.from(null)).toThrow("(Parameter 'collection')");

    expect(() => ConcurrentDictionary.withCapacity(0, 4)).toThrow(
      "The concurrencyLevel argument must be positive, or -1 to indicate a default level. (Parameter 'concurrencyLevel')",
    );

    expect(ConcurrentDictionary.withCapacity(-1, 4).count).toBe(0);

    expect(() => ConcurrentDictionary.withCapacity(2, -1)).toThrow(
      "capacity ('-1') must be a non-negative value. (Parameter 'capacity')",
    );
  });

  test("toJSON, toString and invisible storage", () => {
    const dictionary = new ConcurrentDictionary<number, string>(null, null, [
      Int32Type,
      StringType,
    ]);

    dictionary.set(1, "a");

    expect(JSON.stringify(dictionary)).toBe('{"1":"a"}');

    expect(String(dictionary)).toBe(
      "System.Collections.Concurrent.ConcurrentDictionary`2[System.Int32,System.String]",
    );

    expect(Object.keys(dictionary)).toEqual([]);
    expect(ConcurrentDictionary.$runtimeType.test(dictionary)).toBe(true);
  });
});
