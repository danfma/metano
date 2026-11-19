import { describe, expect, test } from "bun:test";
import { SortedList } from "#/system/collections/sorted-list.ts";
import { createSeededRandom, modelSeeds } from "./model/seeded-random.ts";

// Messages and behaviours read from .NET 10 (2026-11-19).
const MODIFIED = "Collection was modified after the enumerator was instantiated.";
const INDEX = "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')";
const NESTED =
  "This operation is not supported on SortedList nested types because they require modifying the original SortedList.";

const sample = (): SortedList<number, number> =>
  SortedList.of([
    [2, 20],
    [1, 10],
  ]);

function enumerateWhile(list: SortedList<number, number>, mutate: () => void): void {
  for (const _ of list) {
    mutate();
  }
}

describe("SortedList", () => {
  test("keeps keys sorted with index access", () => {
    const list = SortedList.of([
      [3, "c"],
      [1, "a"],
      [2, "b"],
    ]);

    expect([...list]).toEqual([
      [1, "a"],
      [2, "b"],
      [3, "c"],
    ]);
    expect(list.getKeyAtIndex(0)).toBe(1);
    expect(list.getValueAtIndex(2)).toBe("c");
    expect(list.keys.get(1)).toBe(2);
    expect(list.values.get(1)).toBe("b");
    expect(list.indexOfKey(3)).toBe(2);
    expect(list.indexOfKey(9)).toBe(-1);
    expect(list.indexOfValue("b")).toBe(1);
    expect(list.indexOfValue("z")).toBe(-1);
  });

  test("index members throw ArgumentOutOfRangeException with the actual value", () => {
    const list = sample();

    for (const action of [
      () => list.getKeyAtIndex(5),
      () => list.getValueAtIndex(-1),
      () => list.removeAt(5),
      () => list.setValueAtIndex(5, 1),
      () => list.keys.get(5),
      () => list.values.get(-1),
    ]) {
      expect(action).toThrow(INDEX);
    }

    expect(() => list.getKeyAtIndex(5)).toThrow("Actual value was 5.");
  });

  test("get, set, update, add and remove", () => {
    const list = sample();

    expect(list.get(1)).toBe(10);
    expect(() => list.get(9)).toThrow("The given key '9' was not present in the dictionary.");

    list.set(0, 0);
    list.set(1, 11);

    expect([...list.keys]).toEqual([0, 1, 2]);
    expect(list.update(1, (v) => v + 1)).toBe(12);
    expect(() => list.add(1, 5)).toThrow("An item with the same key has already been added. Key: 1 (Parameter 'key')");
    expect(list.remove(0)).toBe(true);
    expect(list.remove(9)).toBe(false);
    expect(list.tryGetValue(2, 0)).toEqual([true, 20]);
    expect(list.tryGetValue(9, 0)).toEqual([false, 0]);
    expect(list.getValueOrDefault(9, -1)).toBe(-1);
  });

  test("the copy constructor rejects keys equal by the comparer", () => {
    const ignoreCase = { compare: (x: string, y: string): number => x.toLowerCase().localeCompare(y.toLowerCase()) };

    expect(() =>
      SortedList.from(
        [
          ["a", 1],
          ["A", 2],
        ],
        ignoreCase,
      ),
    ).toThrow(/^An item with the same key has already been added\. Key: A$/);
    expect(() => SortedList.from(null)).toThrow("(Parameter 'dictionary')");
  });

  test("withCapacity validates the capacity, as .NET", () => {
    expect(SortedList.withCapacity<number, number>(5).count).toBe(0);
    expect(() => SortedList.withCapacity(-1)).toThrow("capacity ('-1') must be a non-negative value. (Parameter 'capacity')");
  });

  test("null keys throw ArgumentNullException (key)", () => {
    const list = new SortedList<string | null, number>();

    for (const action of [() => list.add(null, 1), () => list.indexOfKey(null), () => list.get(null), () => list.set(null, 1)]) {
      expect(action).toThrow("Value cannot be null. (Parameter 'key')");
    }
  });

  test("version: remove of an absent key does not invalidate; every other write does", () => {
    const quiet = sample();

    expect(() => enumerateWhile(quiet, () => quiet.remove(9))).not.toThrow();

    for (const mutate of [
      (l: SortedList<number, number>) => l.set(1, 5),
      (l: SortedList<number, number>) => l.clear(),
      (l: SortedList<number, number>) => l.setValueAtIndex(0, 7),
      (l: SortedList<number, number>) => l.add(9, 9),
    ]) {
      const list = sample();

      expect(() => enumerateWhile(list, () => mutate(list))).toThrow(MODIFIED);
    }

    const list = sample();

    expect(() => {
      for (const key of list.keys) {
        list.set(key, 9);
      }
    }).toThrow(MODIFIED);
  });

  test("keys and values are live read-only lists", () => {
    const list = sample();
    const keys = list.keys;

    list.add(0, 0);

    expect(list.keys).toBe(keys);
    expect([...keys]).toEqual([0, 1, 2]);
    expect(keys.isReadOnly).toBe(true);
    expect(keys.indexOf(2)).toBe(2);
    expect(list.values.contains(20)).toBe(true);
    expect(keys.fromEnd(1)).toBe(2);
    expect(() => keys.set(0, 3)).toThrow("Mutating a key collection derived from a dictionary is not allowed.");
    expect(() => list.values.set(0, 3)).toThrow(NESTED);

    for (const view of [list.keys, list.values]) {
      expect(() => view.add(5)).toThrow(NESTED);
      expect(() => view.insert(0, 5)).toThrow(NESTED);
      expect(() => view.remove(1)).toThrow(NESTED);
      expect(() => view.removeAt(0)).toThrow(NESTED);
      expect(() => view.clear()).toThrow(NESTED);
      expect(() => view.copyTo([0])).toThrow(
        "Destination array was not long enough. Check the destination index, length, and the array's lower bounds.",
      );
    }
  });

  test("a comparer failure is wrapped as Array.BinarySearch wraps it (part-1 review F2)", () => {
    const failure = new Error("cannot compare");
    const throwing = {
      compare: (): number => {
        throw failure;
      },
    };
    const list = SortedList.of([[1, 1]], throwing);

    expect(() => list.add(2, 2)).toThrow("Failed to compare two elements in the array.");

    let caught: unknown = null;

    try {
      list.containsKey(2);
    } catch (error) {
      caught = error;
    }

    expect((caught as Error).name).toBe("InvalidOperationException");
    expect((caught as { innerException: unknown }).innerException).toBe(failure);

    expect(() => new SortedList<object, number>([[{}, 1], [{}, 2]])).toThrow(
      "Failed to compare two elements in the array.",
    );
  });

  test("toJSON and invisible storage", () => {
    const list = sample();

    expect(JSON.stringify(list)).toBe('{"1":10,"2":20}');
    expect(JSON.stringify(list.keys)).toBe("[1,2]");
    expect(Object.keys(list)).toEqual([]);
  });

  test("the runtime type is a data literal", () => {
    expect(SortedList.$runtimeType.collection).toBe("SortedList");
    expect(SortedList.$runtimeType.test(sample())).toBe(true);
  });
});

for (const seed of modelSeeds()) {
  test(`SortedList agrees with a sorted model (seed ${seed})`, () => {
    const random = createSeededRandom(seed);
    const list = new SortedList<number, number>();
    const model = new Map<number, number>();

    for (let step = 0; step < 2000; step++) {
      const key = random.below(80);

      if (random.next() < 0.6) {
        list.set(key, step);
        model.set(key, step);
      } else {
        expect(list.remove(key)).toBe(model.delete(key));
      }

      if (step % 50 === 0) {
        const sorted = [...model].sort(([x], [y]) => x - y);

        expect([...list]).toEqual(sorted);
        expect([...list.keys]).toEqual(sorted.map(([k]) => k));
      }
    }
  });
}
