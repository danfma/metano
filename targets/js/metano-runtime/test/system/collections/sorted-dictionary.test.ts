import { describe, expect, test } from "bun:test";
import { SortedDictionary } from "#/system/collections/sorted-dictionary.ts";

// Messages and behaviours read from .NET 10 (2026-11-19).
const MODIFIED = "Collection was modified after the enumerator was instantiated.";

const sample = (): SortedDictionary<number, number> =>
  SortedDictionary.of([
    [2, 20],
    [1, 10],
  ]);

function enumerateWhile(dictionary: SortedDictionary<number, number>, mutate: () => void): void {
  for (const _ of dictionary) {
    mutate();
  }
}

describe("SortedDictionary", () => {
  test("enumerates entries, keys and values in key order", () => {
    const dictionary = SortedDictionary.of([
      [3, "c"],
      [1, "a"],
      [2, "b"],
    ]);

    expect([...dictionary]).toEqual([
      [1, "a"],
      [2, "b"],
      [3, "c"],
    ]);
    expect([...dictionary.keys]).toEqual([1, 2, 3]);
    expect([...dictionary.values]).toEqual(["a", "b", "c"]);
    expect(dictionary.keys.count).toBe(3);
    expect(dictionary.values.contains("b")).toBe(true);
  });

  test("keys and values are live, cached views", () => {
    const dictionary = sample();
    const keys = dictionary.keys;

    dictionary.set(0, 0);

    expect(dictionary.keys).toBe(keys);
    expect([...keys]).toEqual([0, 1, 2]);
  });

  test("get, set, update and tryGetValue", () => {
    const dictionary = sample();

    expect(dictionary.get(1)).toBe(10);
    expect(() => dictionary.get(9)).toThrow("The given key '9' was not present in the dictionary.");

    dictionary.set(1, 11);
    expect(dictionary.get(1)).toBe(11);
    expect(dictionary.update(1, (v) => v + 1)).toBe(12);
    expect(dictionary.updateReturningOld(1, (v) => v + 1)).toBe(12);
    expect(dictionary.get(1)).toBe(13);
    expect(() => dictionary.update(9, (v) => v)).toThrow("The given key '9' was not present in the dictionary.");
    expect(dictionary.tryGetValue(2, 0)).toEqual([true, 20]);
    expect(dictionary.tryGetValue(9, 0)).toEqual([false, 0]);
    expect(dictionary.getValueOrDefault(9, -1)).toBe(-1);
    expect(dictionary.containsKey(2)).toBe(true);
    expect(dictionary.containsValue(20)).toBe(true);
    expect(dictionary.containsValue(99)).toBe(false);
  });

  test("add of a present key throws with the pair, as .NET", () => {
    const dictionary = SortedDictionary.of([["a", "x"]]);

    expect(() => dictionary.add("a", "y")).toThrow("An item with the same key has already been added. Key: [a, y]");
    expect(() =>
      SortedDictionary.of([
        [1, 1],
        [1, 2],
      ]),
    ).toThrow("Key: [1, 2]");
  });

  test("assign keeps the last value", () => {
    const dictionary = SortedDictionary.assign([
      [1, 1],
      [1, 2],
    ]);

    expect([...dictionary]).toEqual([[1, 2]]);
  });

  test("null keys throw ArgumentNullException (key)", () => {
    const dictionary = new SortedDictionary<string | null, number>();

    for (const action of [
      () => dictionary.get(null),
      () => dictionary.set(null, 1),
      () => dictionary.add(null, 1),
      () => dictionary.remove(null),
      () => dictionary.containsKey(null),
      () => dictionary.tryGetValue(null, 0),
    ]) {
      expect(action).toThrow("Value cannot be null. (Parameter 'key')");
    }
  });

  test("from(null) throws ArgumentNullException (dictionary)", () => {
    expect(() => SortedDictionary.from(null)).toThrow("(Parameter 'dictionary')");
  });

  test("every add, set, remove and clear invalidates, a no-op included", () => {
    for (const mutate of [
      (d: SortedDictionary<number, number>) => d.set(1, 5),
      (d: SortedDictionary<number, number>) => d.remove(9),
      (d: SortedDictionary<number, number>) => d.clear(),
      (d: SortedDictionary<number, number>) => d.add(7, 7),
    ]) {
      const dictionary = sample();

      expect(() => enumerateWhile(dictionary, () => mutate(dictionary))).toThrow(MODIFIED);
    }

    const dictionary = sample();

    expect(() => enumerateWhile(dictionary, () => dictionary.tryGetValue(1, 0))).not.toThrow();
  });

  test("the key and value views follow the dictionary's rule", () => {
    const dictionary = sample();

    expect(() => {
      for (const key of dictionary.keys) {
        dictionary.set(key, 9);
      }
    }).toThrow(MODIFIED);
  });

  test("the comparer orders and identifies keys", () => {
    const ignoreCase = { compare: (x: string, y: string): number => x.toLowerCase().localeCompare(y.toLowerCase()) };
    const dictionary = SortedDictionary.of([["B", 1]], ignoreCase);

    dictionary.set("a", 2);
    dictionary.set("b", 3);

    expect([...dictionary]).toEqual([
      ["a", 2],
      ["B", 3],
    ]);
    expect(dictionary.comparer).toBe(ignoreCase);
  });

  test("copyTo, toJSON and invisible storage", () => {
    const dictionary = sample();
    const target: (readonly [number, number])[] = [
      [0, 0],
      [0, 0],
    ];

    dictionary.copyTo(target);

    expect(target).toEqual([
      [1, 10],
      [2, 20],
    ]);
    expect(() => dictionary.copyTo([[0, 0]])).toThrow("Destination array is not long enough");
    expect(JSON.stringify(dictionary)).toBe('{"1":10,"2":20}');
    expect(Object.keys(dictionary)).toEqual([]);
  });

  test("the runtime type is a data literal", () => {
    expect(SortedDictionary.$runtimeType.collection).toBe("SortedDictionary");
    expect(SortedDictionary.$runtimeType.test(sample())).toBe(true);
  });
});
