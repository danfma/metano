import { describe, expect, test } from "bun:test";
import type { IList } from "#/system/collections/interfaces.ts";
import { List } from "#/system/collections/list.ts";
import { asReadOnly, ReadOnlyList } from "#/system/collections/read-only-list.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

// Messages and behaviours read from .NET 10 (2026-11-19).
const READ_ONLY = "Collection is read-only.";

const INDEX =
  "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')";

describe("ReadOnlyList", () => {
  test("is a live view of a List", () => {
    const list = List.of([1, 2]);
    const view = asReadOnly(list);

    list.add(3);

    expect(view.count).toBe(3);
    expect([...view]).toEqual([1, 2, 3]);
    expect(view.get(2)).toBe(3);
    expect(view.fromEnd(1)).toBe(3);
    expect(view.indexOf(2)).toBe(1);
    expect(view.contains(9)).toBe(false);
    expect(view).not.toBeInstanceOf(List);
  });

  test("is a live view of an array", () => {
    const array = [4, 5];
    const view = ReadOnlyList.fromArray(array);

    array.push(6);

    expect(view.count).toBe(3);
    expect(view.indexOf(5)).toBe(1);
    expect(view.contains(6)).toBe(true);
    expect(view.fromEnd(1)).toBe(6);
  });

  test("an out-of-range index throws ArgumentOutOfRangeException over a list and an array", () => {
    expect(() => new ReadOnlyList([1]).get(5)).toThrow(INDEX);
    expect(() => asReadOnly(List.of([1])).get(5)).toThrow(INDEX);
    expect(() => new ReadOnlyList([1, 2]).fromEnd(3)).toThrow(INDEX);
  });

  test("every mutator throws NotSupportedException", () => {
    const view = new ReadOnlyList([1]);

    for (const action of [
      () => view.set(0, 3),
      () => view.update(0, (v) => v),
      () => view.updateReturningOld(0, (v) => v),
      () => view.add(2),
      () => view.insert(0, 2),
      () => view.remove(1),
      () => view.removeAt(0),
      () => view.clear(),
    ]) {
      expect(action).toThrow(READ_ONLY);
    }

    expect(view.isReadOnly).toBe(true);
  });

  test("enumeration follows the source's rule", () => {
    const list = List.of([1, 2]);
    const view = asReadOnly(list);

    expect(() => {
      for (const _ of view) {
        list.add(3);
      }
    }).toThrow("Collection was modified; enumeration operation may not execute.");

    const array = [1, 2];

    expect(() => {
      for (const _ of new ReadOnlyList(array)) {
        array[0] = 9;
      }
    }).not.toThrow();
  });

  test("null sources throw as .NET", () => {
    expect(() => new ReadOnlyList(null as never)).toThrow(
      "Value cannot be null. (Parameter 'list')",
    );

    expect(() => ReadOnlyList.fromArray(null as never)).toThrow(
      "Value cannot be null. (Parameter 'array')",
    );

    expect(() => asReadOnly(null as never)).toThrow(
      "Object reference not set to an instance of an object.",
    );
  });

  test("copyTo is the source's", () => {
    const target = [0, 0, 0];

    new ReadOnlyList([4, 5]).copyTo(target, 1);

    expect(target).toEqual([0, 4, 5]);

    expect(() => new ReadOnlyList([4, 5]).copyTo([0], 0)).toThrow(
      "Destination array was not long enough",
    );

    expect(() => asReadOnly(List.of([4, 5])).copyTo([0], 0)).toThrow(
      "Destination array was not long enough",
    );

    expect(() => new ReadOnlyList([4, 5]).copyTo([0, 0, 0, 0, 0], -1)).toThrow(
      "destinationIndex ('-1') must be greater than or equal to '0'.",
    );
  });

  test("empty is one shared instance", () => {
    expect(ReadOnlyList.empty()).toBe(ReadOnlyList.empty());
    expect(ReadOnlyList.empty().count).toBe(0);
  });

  test("toJSON, toString and invisible storage", () => {
    const view = new ReadOnlyList([1, 2], [Int32Type]);

    expect(JSON.stringify(view)).toBe("[1,2]");
    expect(String(view)).toBe("System.Collections.ObjectModel.ReadOnlyCollection`1[System.Int32]");
    expect(Object.keys(view)).toEqual([]);
    expect(ReadOnlyList.$runtimeType.test(view)).toBe(true);
  });

  test("length, toArray and asJsArray across list, array, nested and custom sources", () => {
    const list = List.of([1, 2]);
    const overList = asReadOnly(list);

    expect(overList.length).toBe(2);
    expect(overList.asJsArray()).toBe(list.asJsArray());

    list.add(3);

    expect(overList.length).toBe(3);
    expect(overList.asJsArray()).toEqual([1, 2, 3]);

    const copiedFromList = overList.toArray();
    copiedFromList[0] = 99;
    expect(list.get(0)).toBe(1);

    const raw = [4, 5];
    const overArray = ReadOnlyList.fromArray(raw);

    expect(overArray.length).toBe(2);
    expect(overArray.asJsArray()).toBe(raw);

    raw.push(6);

    expect(overArray.length).toBe(3);
    expect(overArray.asJsArray()).toEqual([4, 5, 6]);

    const copiedFromArray = overArray.toArray();
    copiedFromArray[0] = 88;
    expect(raw[0]).toBe(4);

    const nested = new ReadOnlyList(overList);

    expect(nested.length).toBe(3);
    expect(nested.asJsArray()).toBe(list.asJsArray());
    expect(ReadOnlyList.empty<number>().asJsArray()).toEqual([]);

    const custom: IList<number> = {
      count: 2,
      isReadOnly: true,
      get: (i) => i + 10,
      set: () => {},
      indexOf: () => -1,
      insert: () => {},
      removeAt: () => {},
      add: () => {},
      clear: () => {},
      contains: () => false,
      copyTo: () => {},
      remove: () => false,
      [Symbol.iterator]: () => [10, 11][Symbol.iterator](),
    };

    const overCustom = new ReadOnlyList(custom);

    expect(overCustom.length).toBe(2);
    expect(overCustom.asJsArray()).toEqual([10, 11]);
    expect(overCustom.toArray()).toEqual([10, 11]);
  });
});
