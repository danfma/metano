import { describe, expect, test } from "bun:test";
import { SortedSet } from "#/system/collections/sorted-set.ts";
import { createSeededRandom, modelSeeds } from "./model/seeded-random.ts";

// Messages and behaviours read from .NET 10 (2026-11-19).
const MODIFIED = "Collection was modified after the enumerator was instantiated.";

const byMagnitude = { compare: (x: number, y: number): number => Math.abs(x) - Math.abs(y) };

function enumerateWhile(set: SortedSet<number>, mutate: () => void): void {
  for (const _ of set) {
    mutate();
  }
}

describe("SortedSet", () => {
  test("orders and deduplicates by the comparer", () => {
    expect([...new SortedSet([3, 1, 3, 2])]).toEqual([1, 2, 3]);
    expect([...SortedSet.of([-3, 2, 3], byMagnitude)]).toEqual([2, -3]);
  });

  test("the comparer alone decides equality", () => {
    const set = SortedSet.of([-3], byMagnitude);

    expect(set.contains(3)).toBe(true);
    expect(set.add(3)).toBe(false);
    expect(set.tryGetValue(3, 0)).toEqual([true, -3]);
    expect(set.tryGetValue(4, 0)).toEqual([false, 0]);
  });

  test("comparer is the argument or the default comparer", () => {
    expect(SortedSet.of([], byMagnitude).comparer).toBe(byMagnitude);
    expect(new SortedSet<number>().comparer.compare(1, 2)).toBeLessThan(0);
  });

  test("from(null) throws ArgumentNullException (collection)", () => {
    expect(() => SortedSet.from(null)).toThrow("Value cannot be null. (Parameter 'collection')");
  });

  test("null sorts first", () => {
    const set = new SortedSet<string | null>(["b", null, "a"]);

    expect([...set]).toEqual([null, "a", "b"]);
    expect(set.minOr("x")).toBeNull();
  });

  test("reference-type minOr / maxOr accept null without widening the stored element", () => {
    const set = new SortedSet<string>();
    const minimum: string | null = set.minOr(null);
    const maximum: string | null = set.maxOr(null);

    expect(minimum).toBeNull();
    expect(maximum).toBeNull();

    set.add("a");

    expect(set.minOr(null)).toBe("a");
    expect(set.maxOr(null)).toBe("a");
  });

  test("an element the default comparer cannot order throws ArgumentException", () => {
    const set = new SortedSet<object>([{}]);

    expect(() => set.add({})).toThrow("At least one object must implement IComparable.");
  });

  test("minOr / maxOr return the caller's default when empty", () => {
    const set = new SortedSet<number>();

    expect(set.minOr(0)).toBe(0);
    expect(set.maxOr(0)).toBe(0);

    set.add(5);
    set.add(2);

    expect(set.minOr(0)).toBe(2);
    expect(set.maxOr(0)).toBe(5);
  });

  test("add, remove and clear always invalidate a running enumeration", () => {
    for (const mutate of [
      (s: SortedSet<number>) => s.add(1),
      (s: SortedSet<number>) => s.remove(9),
      (s: SortedSet<number>) => s.clear(),
    ]) {
      const set = new SortedSet([1, 2]);

      expect(() => enumerateWhile(set, () => mutate(set))).toThrow(MODIFIED);
    }
  });

  test("set operations invalidate only when they change the set, except intersectWith", () => {
    const quiet = [
      (s: SortedSet<number>) => s.unionWith([]),
      (s: SortedSet<number>) => s.unionWith([1]),
      (s: SortedSet<number>) => s.removeWhere((v) => v > 9),
      (s: SortedSet<number>) => s.exceptWith([9]),
      (s: SortedSet<number>) => s.symmetricExceptWith([]),
    ];

    for (const mutate of quiet) {
      const set = new SortedSet([1, 2]);

      expect(() => enumerateWhile(set, () => mutate(set))).not.toThrow();
    }

    const loud = [
      (s: SortedSet<number>) => s.intersectWith([1, 2]),
      (s: SortedSet<number>) => s.intersectWith([]),
      (s: SortedSet<number>) => s.unionWith([7]),
      (s: SortedSet<number>) => s.exceptWith([2]),
      (s: SortedSet<number>) => s.symmetricExceptWith([7]),
    ];

    for (const mutate of loud) {
      const set = new SortedSet([1, 2]);

      expect(() => enumerateWhile(set, () => mutate(set))).toThrow(MODIFIED);
    }
  });

  test(
    "remove on an empty set, intersectWith(itself) and an empty view's clear change nothing (part-1 review F1)",
    () => {
      const empty = new SortedSet<number>();

      expect(() => enumerateWhile(empty, () => empty.remove(1))).not.toThrow();

      const set = new SortedSet([1, 2]);

      expect(() => enumerateWhile(set, () => set.intersectWith(set))).not.toThrow();

      const view = set.getViewBetween(5, 9);

      expect(() => enumerateWhile(set, () => view.clear())).not.toThrow();
    },
  );

  test("intersectWith on an empty set changes nothing", () => {
    const set = new SortedSet<number>();

    set.intersectWith([1]);

    expect(() => enumerateWhile(set, () => {})).not.toThrow();
  });

  test("set operations", () => {
    const set = new SortedSet([1, 2, 3]);

    set.unionWith([5, 4]);
    expect([...set]).toEqual([1, 2, 3, 4, 5]);

    set.intersectWith([2, 3, 4, 9]);
    expect([...set]).toEqual([2, 3, 4]);

    set.exceptWith([3]);
    expect([...set]).toEqual([2, 4]);

    set.symmetricExceptWith([4, 6, 6]);
    expect([...set]).toEqual([2, 6]);

    set.symmetricExceptWith(set);
    expect(set.count).toBe(0);
  });

  test("set predicates deduplicate the other sequence by the set's comparer", () => {
    const set = new SortedSet([1, 2]);

    expect(set.setEquals([2, 1, 1])).toBe(true);
    expect(set.isSubsetOf([1, 2, 3])).toBe(true);
    expect(set.isProperSubsetOf([1, 2, 2])).toBe(false);
    expect(set.isSupersetOf([1])).toBe(true);
    expect(set.isProperSupersetOf([1, 1])).toBe(true);
    expect(set.overlaps([5, 2])).toBe(true);
    expect(SortedSet.of([-1], byMagnitude).setEquals([1])).toBe(true);
  });

  test("a null other throws ArgumentNullException (other)", () => {
    const set = new SortedSet([1]);

    expect(() => set.unionWith(null as never)).toThrow("(Parameter 'other')");
    expect(() => set.isSubsetOf(null as never)).toThrow("(Parameter 'other')");
  });

  test("removeWhere returns the number removed", () => {
    const set = new SortedSet([1, 2, 3, 4]);

    expect(set.removeWhere((v) => v % 2 === 0)).toBe(2);
    expect([...set]).toEqual([1, 3]);
  });

  test("reverse enumerates descending and is version-checked", () => {
    const set = new SortedSet([1, 2, 3]);

    expect([...set.reverse()]).toEqual([3, 2, 1]);

    expect(() => {
      for (const _ of set.reverse()) {
        set.add(5);
      }
    }).toThrow(MODIFIED);
  });

  test("copyTo validates as .NET", () => {
    const set = new SortedSet([1, 2, 3]);
    const target = [0, 0, 0, 0];

    set.copyTo(target, 1);

    expect(target).toEqual([0, 1, 2, 3]);

    expect(() => set.copyTo([0, 0])).toThrow(
      "Destination array is not long enough to copy all the items in the collection. Check array index and length.",
    );

    expect(() => set.copyTo([0, 0, 0, 0, 0], -1)).toThrow(
      "index ('-1') must be a non-negative value. (Parameter 'index')",
    );
  });

  test("storage is invisible to keys, spread and JSON", () => {
    const set = new SortedSet([2, 1]);

    expect(Object.keys(set)).toEqual([]);
    expect(JSON.stringify(set)).toBe("[1,2]");
    expect(Array.isArray(set)).toBe(false);
  });

  test("the runtime type is a data literal", () => {
    const type = SortedSet.$runtimeType;

    expect(type).toBe(SortedSet.$runtimeType);
    expect(type.collection).toBe("SortedSet");
    expect(type.test(new SortedSet())).toBe(true);
    expect(type.test([])).toBe(false);
  });
});

describe("SortedSet views", () => {
  test("a view is live and bounded", () => {
    const set = new SortedSet([1, 2, 3, 4, 5]);
    const view = set.getViewBetween(2, 4);

    expect(view).toBeInstanceOf(SortedSet);
    expect([...view]).toEqual([2, 3, 4]);

    set.add(10);
    set.remove(3);

    expect([...view]).toEqual([2, 4]);
    expect(view.count).toBe(2);
    expect(view.minOr(0)).toBe(2);
    expect(view.maxOr(0)).toBe(4);
    expect([...view.reverse()]).toEqual([4, 2]);
  });

  test("an empty view's min and max are the caller's default", () => {
    const view = new SortedSet([1, 9]).getViewBetween(2, 4);

    expect([view.minOr(0), view.maxOr(0), view.count]).toEqual([0, 0, 0]);
  });

  test("a view writes through to its set", () => {
    const set = new SortedSet([1, 5]);
    const view = set.getViewBetween(2, 4);

    view.add(3);

    expect([...set]).toEqual([1, 3, 5]);
    expect(view.count).toBe(1);
  });

  test("outside its range a view does not contain, does not remove and cannot add", () => {
    const set = new SortedSet([1, 2, 3, 4, 5, 6]);
    const view = set.getViewBetween(2, 3);

    expect(view.contains(5)).toBe(false);
    expect(view.remove(5)).toBe(false);
    expect(set.count).toBe(6);

    expect(() => view.add(9)).toThrow(
      "Specified argument was out of the range of valid values. (Parameter 'item')",
    );
  });

  test("clear and removeWhere on a view touch only its range", () => {
    const set = new SortedSet([1, 2, 3, 4, 5, 6]);

    set.getViewBetween(2, 3).clear();
    expect([...set]).toEqual([1, 4, 5, 6]);

    expect(set.getViewBetween(4, 5).removeWhere((v) => v % 2 === 0)).toBe(1);
    expect([...set]).toEqual([1, 5, 6]);
  });

  test("view bounds are checked as .NET", () => {
    const set = new SortedSet([1, 2, 3, 4, 5, 6]);
    const view = set.getViewBetween(2, 5);

    expect(() => set.getViewBetween(3, 1)).toThrow(
      "Must be less than or equal to upperValue. (Parameter 'lowerValue')",
    );

    expect(() => view.getViewBetween(3, 9)).toThrow("(Parameter 'upperValue')");
    expect(() => view.getViewBetween(1, 4)).toThrow("(Parameter 'lowerValue')");
    expect([...view.getViewBetween(3, 4)]).toEqual([3, 4]);
  });

  test("a view and its set share one version", () => {
    const set = new SortedSet([1, 2, 3, 4, 5]);
    const view = set.getViewBetween(2, 4);

    expect(() => enumerateWhile(set, () => view.remove(3))).toThrow(MODIFIED);
    expect(() => enumerateWhile(view, () => set.add(99))).toThrow(MODIFIED);
  });
});

for (const seed of modelSeeds()) {
  test(`SortedSet agrees with a sorted-array model (seed ${seed})`, () => {
    const random = createSeededRandom(seed);
    const set = new SortedSet<number>();
    const model = new Set<number>();

    for (let step = 0; step < 3000; step++) {
      const value = random.below(100);

      if (random.next() < 0.6) {
        expect(set.add(value)).toBe(!model.has(value));
        model.add(value);
      } else {
        expect(set.remove(value)).toBe(model.delete(value));
      }

      if (step % 100 === 0) {
        const sorted = [...model].sort((x, y) => x - y);
        const low = random.below(50);
        const high = low + random.below(50);

        expect([...set]).toEqual(sorted);
        expect(set.count).toBe(sorted.length);

        expect([...set.getViewBetween(low, high)]).toEqual(
          sorted.filter((v) => v >= low && v <= high),
        );
      }
    }
  });
}
