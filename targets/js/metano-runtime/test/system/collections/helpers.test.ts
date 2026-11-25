import {
  describe,
  expect,
  test,
} from "bun:test";

import {
  asArray,
  asJsArray,
  asList,
  asReadOnlyJsArray,
  asReadOnlySpan,
  asSpan,
  fromEnd,
  isIterable,
} from "#/system/collections/helpers.ts";

import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableArrayBuilder } from "#/system/collections/immutable-array-builder.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableListBuilder } from "#/system/collections/immutable-list-builder.ts";
import type { IReadOnlyList } from "#/system/collections/interfaces.ts";
import { List } from "#/system/collections/list.ts";
import { ReadOnlyList } from "#/system/collections/read-only-list.ts";
import { SortedList } from "#/system/collections/sorted-list.ts";
import { IndexOutOfRangeException } from "#/system/exceptions/index-out-of-range-exception.ts";
import { InvalidCastException } from "#/system/exceptions/invalid-cast-exception.ts";

const LIST_OF_INT32 = "System.Collections.Generic.IList`1[System.Int32]";

describe("helpers", () => {
  test("fromEnd on a native array is checked", () => {
    expect(fromEnd([1, 2, 3], 1)).toBe(3);
    expect(() => fromEnd([1], 2)).toThrow(IndexOutOfRangeException);
    expect(() => fromEnd([1], 0)).toThrow("Index was outside the bounds of the array.");
  });

  test("isIterable", () => {
    expect(isIterable("ab")).toBe(true);
    expect(isIterable(List.of([1]))).toBe(true);
    expect(isIterable({})).toBe(false);
    expect(isIterable(null)).toBe(false);
  });

  test("asArray unwraps arrays and fixed-size adopted lists only", () => {
    const array = [1];

    expect(asArray(array)).toBe(array);
    expect(asArray(List.adopt(array, { fixedSize: true }))).toBe(array);
    expect(asArray(List.of([1]))).toBeNull();
    expect(asArray("x")).toBeNull();
  });

  test("asList adopts a native array as a fixed-size list", () => {
    const array = [1, 2];
    const list = asList<number>(array, LIST_OF_INT32);

    expect(list).toBeInstanceOf(List);
    expect(list?.count).toBe(2);
    expect(list?.isReadOnly).toBe(true);
    expect(asArray(list)).toBe(array);

    list?.set(0, 9);

    expect(array[0]).toBe(9);
    expect(() => list?.add(3)).toThrow("Collection was of a fixed size.");
  });

  test("asList passes null, a List and any other object through", () => {
    const list = List.of([1]);
    const shaped = { count: 0 };

    expect(asList(null, LIST_OF_INT32)).toBeNull();
    expect(asList(undefined, LIST_OF_INT32)).toBeNull();
    expect(asList<number>(list, LIST_OF_INT32)).toBe(list);
    expect(Object.is(asList(shaped, LIST_OF_INT32), shaped)).toBe(true);
  });

  test("asList rejects a primitive with .NET's InvalidCastException", () => {
    expect(() => asList("ab", "System.Collections.Generic.IList`1[System.Char]")).toThrow(
      new InvalidCastException(
        "Unable to cast object of type 'System.String' to type " +
          "'System.Collections.Generic.IList`1[System.Char]'.",
      ),
    );

    expect(() => asList(5, LIST_OF_INT32)).toThrow(
      "Unable to cast object of type 'System.Int32' to type " +
        "'System.Collections.Generic.IList`1[System.Int32]'.",
    );

    expect(() => asList(true, LIST_OF_INT32)).toThrow(InvalidCastException);
    expect(() => asList(1n, LIST_OF_INT32)).toThrow(InvalidCastException);
  });

  test("the views return the backing array; null handling", () => {
    const list = List.of([1]);

    expect(asJsArray(list)).toBe(list.asJsArray());
    expect(asReadOnlyJsArray(list)).toBe(list.asReadOnlyJsArray());
    expect(asSpan(list)).toBe(list.asJsArray());
    expect(asReadOnlySpan(list)).toBe(list.asJsArray());
    expect(asSpan(null)).toEqual([]);
    expect(asReadOnlySpan(undefined)).toEqual([]);
    expect(() => asJsArray(null)).toThrow("Value cannot be null. (Parameter 'list')");
    expect(() => asJsArray(undefined)).toThrow("Value cannot be null. (Parameter 'list')");
    expect(() => asReadOnlyJsArray(null)).toThrow("Value cannot be null. (Parameter 'list')");
    expect(() => asReadOnlyJsArray(undefined)).toThrow("Value cannot be null. (Parameter 'list')");
  });

  test("asReadOnlyJsArray accepts every IReadOnlyList and native arrays", () => {
    const list = List.of([1, 2]);
    const roList: IReadOnlyList<number> = new ReadOnlyList(list);
    const immutableList = ImmutableList.of([3, 4]);
    const immutableArray = ImmutableArray.of([5, 6]);

    const sorted = SortedList.of([
      [1, "a"],
      [2, "b"],
    ]);

    const listBuilder = new ImmutableListBuilder([7, 8]);
    const arrayBuilder = new ImmutableArrayBuilder([9, 10]);
    const raw = [11, 12];

    expect(asReadOnlyJsArray(list)).toBe(list.asJsArray());
    expect(asReadOnlyJsArray(roList)).toBe(list.asJsArray());
    expect(asReadOnlyJsArray(immutableList)).toBe(immutableList._items);
    expect(asReadOnlyJsArray(immutableArray)).toBe(immutableArray._items);
    expect(asReadOnlyJsArray(sorted.keys)).toBe(sorted._keys);
    expect(asReadOnlyJsArray(listBuilder)).toBe(listBuilder._list.asJsArray());
    expect(asReadOnlyJsArray(arrayBuilder)).toBe(arrayBuilder._list.asJsArray());
    expect(asReadOnlyJsArray(raw)).toBe(raw);
    expect(() => asReadOnlyJsArray<number>(null as IReadOnlyList<number> | null)).toThrow(
      "Value cannot be null. (Parameter 'list')",
    );
  });

  test("asReadOnlyJsArray falls back to a snapshot copy when receiver lacks asReadOnlyJsArray", () => {
    const backing = [10, 20, 30];

    const custom = {
      get count() {
        return backing.length;
      },

      get(index: number) {
        return backing[index]!;
      },

      [Symbol.iterator]() {
        return backing[Symbol.iterator]();
      },
    } as unknown as IReadOnlyList<number>;

    const snapshot1 = asReadOnlyJsArray(custom);

    expect(snapshot1).toEqual([10, 20, 30]);

    backing.push(40);

    expect(snapshot1).toEqual([10, 20, 30]);

    const snapshot2 = asReadOnlyJsArray(custom);

    expect(snapshot2).toEqual([10, 20, 30, 40]);
    expect(snapshot2).not.toBe(snapshot1);
  });
});
