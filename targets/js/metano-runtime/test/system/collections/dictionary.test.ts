import { describe, expect, test } from "bun:test";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { KeyCollection, ValueCollection } from "#/system/collections/dictionary-views.ts";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { ArgumentNullException } from "#/system/exceptions/argument-null-exception.ts";
import { KeyNotFoundException } from "#/system/exceptions/key-not-found-exception.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

class Key {
  constructor(readonly id: number) {}

  equals(other: unknown): boolean {
    return other instanceof Key && other.id === this.id;
  }

  hashCode(): number {
    return this.id;
  }

  toString(): string {
    return `Key { Id = ${this.id} }`;
  }
}

const MODIFIED_MESSAGE = "Collection was modified; enumeration operation may not execute.";

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("Dictionary construction", () => {
  test("of adds (a duplicate throws); assign sets (last wins)", () => {
    expect(Dictionary.of([["a", 1]]).get("a")).toBe(1);

    expect(
      Dictionary.assign([
        ["a", 1],
        ["a", 2],
      ]).get("a"),
    ).toBe(2);

    const error = thrown(() =>
      Dictionary.of([
        [1, 1],
        [1, 2],
      ]),
    );

    expect(error).toBeInstanceOf(ArgumentException);
    expect(error.message).toBe("An item with the same key has already been added. Key: 1");
  });

  test("from copies a dictionary, a Map or entries; null throws", () => {
    const source = Dictionary.of([["a", 1]]);

    expect([...Dictionary.from(source)]).toEqual([["a", 1]]);
    expect(Dictionary.from(new Map([["b", 2]])).get("b")).toBe(2);

    expect(thrown(() => Dictionary.from(null as never)).message).toBe(
      "Value cannot be null. (Parameter 'collection')",
    );
  });

  test("withCapacity validates; initAdd / initAssign chain", () => {
    expect(thrown(() => Dictionary.withCapacity(-1)).message).toBe(
      "Non-negative number required. (Parameter 'capacity')",
    );

    expect(Dictionary.withCapacity<string, number>(4).initAdd([["a", 1]]).count).toBe(1);

    expect(
      Dictionary.withCapacity<string, number>(4)
        .initAssign([
          ["a", 1],
          ["a", 3],
        ])
        .get("a"),
    ).toBe(3);
  });

  test("a comparer keys every entry", () => {
    const ignoreCase = {
      equals: (a: string, b: string) => a.toLowerCase() === b.toLowerCase(),
      getHashCode: (value: string) => value.toLowerCase().length,
    };

    const dictionary = Dictionary.of([["A", 1]], ignoreCase);

    expect(dictionary.get("a")).toBe(1);
    expect(dictionary.containsKey("a")).toBe(true);

    expect(thrown(() => dictionary.add("a", 2)).message).toBe(
      "An item with the same key has already been added. Key: a",
    );
  });
});

describe("Dictionary members", () => {
  test("get / set / tryAdd / remove / clear / count", () => {
    const dictionary = new Dictionary<string, number>();

    dictionary.set("a", 1);
    dictionary.set("a", 2);

    expect(dictionary.tryAdd("a", 3)).toBe(false);
    expect(dictionary.tryAdd("b", 3)).toBe(true);
    expect(dictionary.count).toBe(2);
    expect(dictionary.get("a")).toBe(2);
    expect(dictionary.remove("a")).toBe(true);
    expect(dictionary.remove("a")).toBe(false);
    dictionary.clear();
    expect(dictionary.count).toBe(0);
  });

  test("a missing key throws KeyNotFoundException naming the key", () => {
    const error = thrown(() => Dictionary.of([["a", 1]]).get("zz"));

    expect(error).toBeInstanceOf(KeyNotFoundException);
    expect(error.message).toBe("The given key 'zz' was not present in the dictionary.");

    expect(thrown(() => new Dictionary<Key, number>().get(new Key(1))).message).toBe(
      "The given key 'Key { Id = 1 }' was not present in the dictionary.",
    );
  });

  test("a null key throws ArgumentNullException from every keyed member", () => {
    const dictionary = Dictionary.of<string | null, number>([["a", 1]]);

    for (const call of [
      () => dictionary.get(null),
      () => dictionary.set(null, 1),
      () => dictionary.add(null, 1),
      () => dictionary.tryAdd(null, 1),
      () => dictionary.remove(null),
      () => dictionary.containsKey(null),
      () => dictionary.tryGetValue(null, 0),
      () => dictionary.getValueOrDefault(null, 0),
      () => dictionary.update(null, (v) => v),
    ]) {
      const error = thrown(call);

      expect(error).toBeInstanceOf(ArgumentNullException);
      expect(error.message).toBe("Value cannot be null. (Parameter 'key')");
    }
  });

  test("tryGetValue and getValueOrDefault return the C# default on a miss", () => {
    const dictionary = Dictionary.of([["a", 1]]);

    expect(dictionary.tryGetValue("a", 0)).toEqual([true, 1]);
    expect(dictionary.tryGetValue("b", 0)).toEqual([false, 0]);
    expect(dictionary.getValueOrDefault("b", 7)).toBe(7);
    expect(dictionary.getValueOrDefault("b")).toBeNull();
    expect(Dictionary.of([["n", null]]).getValueOrDefault("n", 5)).toBeNull();
  });

  test("update reads (throws on a miss) and writes", () => {
    const dictionary = Dictionary.of([["a", 1]]);

    expect(dictionary.update("a", (v) => v + 1)).toBe(2);
    expect(dictionary.updateReturningOld("a", (v) => v + 1)).toBe(2);
    expect(dictionary.get("a")).toBe(3);
    expect(thrown(() => dictionary.update("b", (v) => v))).toBeInstanceOf(KeyNotFoundException);
  });

  test("record keys address one entry; class keys without equals are distinct", () => {
    const records = new Dictionary<Key, string>();

    records.set(new Key(1), "a");
    records.set(new Key(1), "b");

    expect(records.count).toBe(1);
    expect(records.get(new Key(1))).toBe("b");

    const plain = new Dictionary<object, string>();

    plain.set({}, "a");
    plain.set({}, "b");

    expect(plain.count).toBe(2);
  });

  test("containsValue, toJSON, toMap, toString", () => {
    const dictionary = Dictionary.of([[1, "a"]], null, [Int32Type, StringType]);

    expect(dictionary.containsValue("a")).toBe(true);
    expect(JSON.stringify(dictionary)).toBe('{"1":"a"}');
    expect(dictionary.toMap()).toEqual(new Map([[1, "a"]]));

    expect(dictionary.toString()).toBe(
      "System.Collections.Generic.Dictionary`2[System.Int32,System.String]",
    );

    expect(dictionary.keys.toString()).toBe(
      "System.Collections.Generic.Dictionary`2+KeyCollection[System.Int32,System.String]",
    );
  });
});

describe("Dictionary enumeration", () => {
  test("insertion order, also after a removal", () => {
    const dictionary = Dictionary.of([
      [1, 1],
      [2, 2],
      [3, 3],
    ]);

    dictionary.remove(2);
    dictionary.set(4, 4);

    expect([...dictionary.keys]).toEqual([1, 3, 4]);
  });

  test("an insertion invalidates; remove, clear and overwrites do not", () => {
    const inserting = Dictionary.of([
      ["a", 1],
      ["b", 2],
    ]);

    expect(
      thrown(() => {
        for (const [key] of inserting) inserting.set(`x${key}`, 0);
      }).message,
    ).toBe(MODIFIED_MESSAGE);

    for (const mutate of [
      (d: Dictionary<string, number>, key: string) => d.remove(key),
      (d: Dictionary<string, number>) => d.clear(),
      (d: Dictionary<string, number>, key: string) => d.set(key, 5),
      (d: Dictionary<string, number>, key: string) => d.update(key, (v) => v + 1),
      (d: Dictionary<string, number>, key: string) => d.tryAdd(key, 9),
    ]) {
      const dictionary = Dictionary.of([
        ["a", 1],
        ["b", 2],
      ]);

      expect(() => {
        for (const [key] of dictionary) mutate(dictionary, key);
      }).not.toThrow();
    }
  });

  test("a removed entry not yet visited is skipped", () => {
    const dictionary = Dictionary.of([
      [1, 1],
      [2, 2],
      [3, 3],
    ]);

    const seen: number[] = [];

    for (const [key] of dictionary) {
      seen.push(key);
      dictionary.remove(2);
    }

    expect(seen).toEqual([1, 3]);
  });
});

describe("KeyCollection / ValueCollection", () => {
  test("live, cached views", () => {
    const dictionary = Dictionary.of([["a", 1]]);
    const keys = dictionary.keys;

    dictionary.set("b", 2);

    expect(dictionary.keys).toBe(keys);
    expect(keys).toBeInstanceOf(KeyCollection);
    expect(dictionary.values).toBeInstanceOf(ValueCollection);
    expect(keys.count).toBe(2);
    expect(keys.contains("b")).toBe(true);
    expect(dictionary.values.contains(2)).toBe(true);
    expect(dictionary.values.toArray()).toEqual([1, 2]);
    expect(JSON.stringify(keys)).toBe('["a","b"]');
  });

  test("their enumeration follows the dictionary's rule", () => {
    const dictionary = Dictionary.of([["a", 1]]);

    expect(() => {
      for (const key of dictionary.keys) dictionary.set(`${key}!`, 0);
    }).toThrow(MODIFIED_MESSAGE);
  });

  test("copyTo", () => {
    const destination = ["", ""];

    Dictionary.of([
      ["a", 1],
      ["b", 2],
    ]).keys.copyTo(destination);

    expect(destination).toEqual(["a", "b"]);
  });
});
