import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import { HashCore } from "#/system/collections/hash-core.ts";

/** A record-like key: equal by `id`, with a hash consistent with it. */
class Key {
  constructor(readonly id: number) {}

  equals(other: unknown): boolean {
    return other instanceof Key && other.id === this.id;
  }

  hashCode(): number {
    return this.id;
  }
}

/** A key with `equals` but no `hashCode` (one bucket, compared with `equals`). */
class EqualsOnly {
  constructor(readonly name: string) {}

  equals(other: unknown): boolean {
    return other instanceof EqualsOnly && other.name === this.name;
  }
}

describe("HashCore contract (the API 011h builds on)", () => {
  test("set reports an insertion; an overwrite keeps the entry's position", () => {
    const core = new HashCore<unknown, string>();

    expect(core.set("a", "1")).toBe(true);
    expect(core.set(new Key(1), "2")).toBe(true);
    expect(core.set("a", "3")).toBe(false);
    expect(core.set(new Key(1), "4")).toBe(false);
    expect([...core]).toEqual([
      ["a", "3"],
      [expect.any(Key), "4"],
    ]);
    expect(core.size).toBe(2);
  });

  test("get returns undefined only on a miss; a stored undefined is found by has", () => {
    const core = new HashCore<unknown, string | undefined>();

    core.set("u", undefined);
    core.set(new Key(2), undefined);

    expect(core.get("u")).toBeUndefined();
    expect(core.has("u")).toBe(true);
    expect(core.has(new Key(2))).toBe(true);
    expect(core.get("missing")).toBeUndefined();
    expect(core.has("missing")).toBe(false);
  });

  test("identity-only keys: two equal-looking objects without equals are two keys", () => {
    const core = new HashCore<object, number>();
    const first = {};

    core.set(first, 1);
    core.set({}, 2);

    expect(core.size).toBe(2);
    expect(core.get(first)).toBe(1);
    expect(core.findKey(first)).toBe(first);
    expect(core.findKey({})).toBeUndefined();
  });

  test("one store mixing primitives and value-equality objects keys each correctly", () => {
    const core = new HashCore<unknown, string>();
    const stored = new Key(1);

    core.set(1, "number");
    core.set("1", "string");
    core.set(1n, "bigint");
    core.set(stored, "record");
    core.set(new Decimal("1.0"), "decimal");
    core.set(new EqualsOnly("x"), "equals-only");
    core.set(Number.NaN, "nan");
    core.set(null, "null");

    expect(core.size).toBe(8);
    expect(core.get(1)).toBe("number");
    expect(core.get("1")).toBe("string");
    expect(core.get(1n)).toBe("bigint");
    expect(core.get(new Key(1))).toBe("record");
    expect(core.findKey(new Key(1))).toBe(stored);
    expect(core.get(new Decimal(1))).toBe("decimal");
    expect(core.get(new EqualsOnly("x"))).toBe("equals-only");
    expect(core.get(Number.NaN)).toBe("nan");
    expect(core.get(null)).toBe("null");
    expect(core.has(new Key(2))).toBe(false);
  });

  test("delete then re-add moves the entry to the end, on both paths", () => {
    const core = new HashCore<unknown, number>();

    core.set("a", 1);
    core.set(new Key(1), 2);
    core.set("b", 3);

    expect(core.delete("a")).toBe(true);
    expect(core.delete(new Key(1))).toBe(true);
    expect(core.delete(new Key(1))).toBe(false);

    core.set(new Key(1), 4);
    core.set("a", 5);

    expect([...core.keys()].map((key) => (key instanceof Key ? `Key${key.id}` : key))).toEqual([
      "b",
      "Key1",
      "a",
    ]);
  });

  test("iteration tolerates mutation the way a Map does", () => {
    const core = new HashCore<unknown, number>();

    core.set(1, 1);
    core.set(new Key(2), 2);
    core.set(3, 3);

    const seen: unknown[] = [];

    for (const [key] of core) {
      seen.push(key instanceof Key ? `Key${key.id}` : key);

      if (key === 1) {
        core.delete(new Key(2));
        core.set(4, 4);
      }
    }

    expect(seen).toEqual([1, 3, 4]);
  });

  test("a user comparer keys every value, primitives included", () => {
    const ignoreCase = {
      equals: (a: string, b: string) => a.toLowerCase() === b.toLowerCase(),
      getHashCode: (value: string) => value.toLowerCase().length,
    };
    const core = new HashCore<string, number>(ignoreCase);

    expect(core.set("A", 1)).toBe(true);
    expect(core.set("a", 2)).toBe(false);
    expect(core.findKey("a")).toBe("A");
    expect(core.get("a")).toBe(2);
    expect(core.delete("a")).toBe(true);
    expect(core.size).toBe(0);
  });

  test("clear empties both paths; clone keeps the comparer and the order", () => {
    const core = new HashCore<unknown, number>();

    core.set("a", 1);
    core.set(new Key(1), 2);

    const copy = core.clone();

    core.clear();

    expect(core.size).toBe(0);
    expect(core.has(new Key(1))).toBe(false);
    expect(copy.size).toBe(2);
    expect(copy.get(new Key(1))).toBe(2);
    expect([...copy.values()]).toEqual([1, 2]);
  });
});
