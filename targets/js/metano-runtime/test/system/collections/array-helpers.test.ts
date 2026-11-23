import {
  describe,
  expect,
  test,
} from "bun:test";

import {
  arrayContains,
  arrayIndexOf,
  arrayLastIndexOf,
  sortArray,
  sortArrayByKeys,
} from "#/system/collections/array-helpers.ts";

import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { ArgumentOutOfRangeException } from "#/system/exceptions/argument-out-of-range-exception.ts";
import { ArgumentNullException } from "#/system/exceptions/argument-null-exception.ts";
import { InvalidOperationException } from "#/system/exceptions/invalid-operation-exception.ts";

class Item {
  constructor(
    readonly name: string,
    readonly quantity: number,
  ) {}

  equals(other: unknown): boolean {
    return other instanceof Item && other.name === this.name && other.quantity === this.quantity;
  }

  hashCode(): number {
    return this.quantity;
  }
}

describe("array-helpers", () => {
  test("sortArray orders numbers numerically, not as text", () => {
    const values = [10, 9, 1, 100, -5];

    sortArray(values);

    expect(values).toEqual([-5, 1, 9, 10, 100]);
  });

  test("sortArray orders bigint, boolean and null as Comparer<T>.Default does", () => {
    const wide = [10n, 9n, 100n];
    const flags = [true, false, true];
    const optional = [3, null, 1];

    sortArray(wide);
    sortArray(flags);
    sortArray(optional);

    expect(wide).toEqual([9n, 10n, 100n]);
    expect(flags).toEqual([false, true, true]);
    expect(optional).toEqual([null, 1, 3]);
  });

  test("sortArray takes a comparison or an IComparer", () => {
    const byComparison = [10, 9, 1];
    const byComparer = [10, 9, 1];

    sortArray(byComparison, (a, b) => b - a);
    sortArray(byComparer, { compare: (a: number, b: number) => b - a });

    expect(byComparison).toEqual([10, 9, 1]);
    expect(byComparer).toEqual([10, 9, 1]);
  });

  test("sortArray rejects null and wraps a throwing comparison", () => {
    expect(() => sortArray(null)).toThrow(ArgumentNullException);
    expect(() =>
      sortArray([2, 1], () => {
        throw new Error("boom");
      }),
    ).toThrow(InvalidOperationException);
  });

  test("sortArrayByKeys permutes the items with the keys", () => {
    const keys = [3, 10, 2];
    const items = ["c", "j", "b"];

    sortArrayByKeys(keys, items);

    expect(keys).toEqual([2, 3, 10]);
    expect(items).toEqual(["b", "c", "j"]);
  });

  test("sortArrayByKeys without items sorts the keys; short items throw", () => {
    const keys = [3, 1];

    sortArrayByKeys(keys, null);

    expect(keys).toEqual([1, 3]);
    expect(() => sortArrayByKeys([2, 1], ["a"])).toThrow(ArgumentException);
    expect(() => sortArrayByKeys(null, [])).toThrow(ArgumentNullException);
  });

  test("searches compare an object by value", () => {
    const items = [new Item("a", 1), new Item("b", 2), new Item("a", 1)];

    expect(arrayIndexOf(items, new Item("b", 2))).toBe(1);
    expect(arrayLastIndexOf(items, new Item("a", 1))).toBe(2);
    expect(arrayContains(items, new Item("a", 1))).toBe(true);
    expect(arrayContains(items, new Item("c", 3))).toBe(false);
  });

  test("searches find NaN and keep primitives by identity", () => {
    expect(arrayIndexOf([1, Number.NaN], Number.NaN)).toBe(1);
    expect(arrayLastIndexOf([Number.NaN, 1], Number.NaN)).toBe(0);
    expect(arrayIndexOf([1, 2, 1], 1, 1)).toBe(2);
    expect(() => arrayIndexOf([1], 1, 5)).toThrow(ArgumentOutOfRangeException);
  });

  test("searches over a string receiver search its characters", () => {
    expect(arrayContains("hello", "e")).toBe(true);
    expect(arrayIndexOf("hello", "l")).toBe(2);
    expect(arrayLastIndexOf("hello", "l")).toBe(3);
  });
});
