import { describe, expect, test } from "bun:test";
import { binarySearch } from "#/system/collections/sorted-array.ts";
import { createSeededRandom, modelSeeds } from "./model/seeded-random.ts";

const byNumber = (x: number, y: number): number => x - y;

describe("binarySearch", () => {
  test("finds a present value", () => {
    expect(binarySearch([1, 3, 5, 7], 0, 4, 5, byNumber)).toBe(2);
  });

  test("returns the complement of the insertion point for an absent value", () => {
    const array = [10, 20, 30];

    expect(binarySearch(array, 0, 3, 5, byNumber)).toBe(~0);
    expect(binarySearch(array, 0, 3, 25, byNumber)).toBe(~2);
    expect(binarySearch(array, 0, 3, 35, byNumber)).toBe(~3);
    expect(binarySearch([], 0, 0, 1, byNumber)).toBe(~0);
  });

  test("searches only the given range", () => {
    const array = [1, 2, 3, 100, 200];

    // SortedList keeps spare slots past its count; they are never read.
    expect(binarySearch(array, 0, 3, 100, byNumber)).toBe(~3);
    expect(binarySearch(array, 3, 2, 200, byNumber)).toBe(4);
    expect(binarySearch(array, 3, 2, 50, byNumber)).toBe(~3);
  });

  test("calls the comparer as compare(element, value), as .NET does", () => {
    const calls: [number, number][] = [];

    binarySearch([1, 2, 3], 0, 3, 3, (x, y) => {
      calls.push([x, y]);

      return x - y;
    });

    expect(calls.every(([, value]) => value === 3)).toBe(true);
    expect(calls[0]).toEqual([2, 3]);
  });

  test("an equal-run search hits the middle-first element, as .NET's", () => {
    // .NET's Array.BinarySearch over [5, 5, 5, 5, 5] for 5 returns 2.
    expect(binarySearch([5, 5, 5, 5, 5], 0, 5, 5, byNumber)).toBe(2);
  });

  for (const seed of modelSeeds()) {
    test(`agrees with a linear scan (seed ${seed})`, () => {
      const random = createSeededRandom(seed);

      for (let round = 0; round < 500; round++) {
        const length = random.below(40);
        const values = new Set<number>();

        while (values.size < length) {
          values.add(random.below(100));
        }

        const array = [...values].sort(byNumber);
        const probe = random.below(110) - 5;
        const index = binarySearch(array, 0, array.length, probe, byNumber);
        const insertion = array.findIndex((value) => value >= probe);
        const expectedInsertion = insertion === -1 ? array.length : insertion;

        if (array[expectedInsertion] === probe) {
          expect(index).toBe(expectedInsertion);
        } else {
          expect(index).toBe(~expectedInsertion);
        }
      }
    });
  }
});
