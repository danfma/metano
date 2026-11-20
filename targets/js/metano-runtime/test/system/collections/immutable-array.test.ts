import { describe, expect, test } from "bun:test";
import { immutableArrayType } from "#/system/collections/descriptors.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

// Messages and same-instance results read from .NET 10 (2026-11-19).
const OUTSIDE = "Index was outside the bounds of the array.";
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";

function caught(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("ImmutableArray", () => {
  const array = (): ImmutableArray<number> => ImmutableArray.of([1, 2, 3]);

  test("is a sibling of ImmutableList, and every result stays an ImmutableArray", () => {
    const original = array();

    expect(original).not.toBeInstanceOf(ImmutableList);
    expect(ImmutableList.of([1])).not.toBeInstanceOf(ImmutableArray);

    for (const result of [
      original.add(4),
      original.insert(0, 0),
      original.removeAt(0),
      original.setItem(0, 9),
      original.sort((a, b) => b - a),
      original.reverse(),
      original.getRange(0, 2),
      original.slice(1),
      original.findAll((v) => v > 1),
      original.clear(),
    ]) {
      expect(result).toBeInstanceOf(ImmutableArray);
    }

    expect([...original.add(4)]).toEqual([1, 2, 3, 4]);
    expect([...original]).toEqual([1, 2, 3]);
  });

  test("the indexer throws IndexOutOfRangeException, a negative index included", () => {
    const original = array();

    for (const action of [
      () => original.get(3),
      () => original.get(-1),
      () => original.fromEnd(4),
      () => original.fromEnd(0),
    ]) {
      const error = caught(action);

      expect(error.name).toBe("IndexOutOfRangeException");
      expect(error.message).toBe(OUTSIDE);
    }

    expect(original.fromEnd(1)).toBe(3);
  });

  test("removeAt / insert keep ArgumentOutOfRangeException", () => {
    expect(caught(() => array().removeAt(3)).name).toBe("ArgumentOutOfRangeException");
    expect(caught(() => array().insert(5, 1)).message).toBe(`${OUT_OF_RANGE} (Parameter 'index')`);
  });

  test("copyTo checks as Array.Copy", () => {
    const original = array();
    const target = [0, 0, 0, 0];

    original.copyTo(target, 1);

    expect(target).toEqual([0, 1, 2, 3]);

    expect(caught(() => original.copyTo(null as never)).message).toBe(
      "Value cannot be null. (Parameter 'destinationArray')",
    );

    expect(caught(() => original.copyTo([0, 0, 0, 0], -1)).message).toBe(
      "destinationIndex ('-1') must be greater than or equal to '0'. (Parameter 'destinationIndex')\nActual value was -1.",
    );

    for (const action of [() => original.copyTo([0, 0]), () => original.copyTo([0, 0, 0], 1)]) {
      const error = caught(action);

      expect(error.name).toBe("ArgumentException");

      expect(error.message).toBe(
        "Destination array was not long enough. Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')",
      );
    }
  });

  test("binarySearch wraps a comparer failure", () => {
    const error = caught(() => ImmutableArray.of([{}, {}]).binarySearch({}));

    expect(error.name).toBe("InvalidOperationException");
    expect(error.message).toBe("Failed to compare two elements in the array.");
    expect(ImmutableArray.of([1, 3, 5]).binarySearch(4)).toBe(~2);
  });

  test("sort returns the receiver when already in order; the pre-scan failure is unwrapped", () => {
    const sorted = array();
    const unsorted = ImmutableArray.of([3, 1, 2]);

    expect(sorted.sort()).toBe(sorted);
    expect(unsorted.sort()).not.toBe(unsorted);
    expect([...unsorted.sort()]).toEqual([1, 2, 3]);
    expect(caught(() => ImmutableArray.of([{}, {}]).sort()).name).toBe("ArgumentException");

    const failing = (): number => {
      throw new RangeError("x");
    };

    expect(caught(() => sorted.sort(failing))).toBeInstanceOf(RangeError);
  });

  test("indexOf names its start parameter startIndex", () => {
    expect(caught(() => array().indexOf(1, -1)).message).toBe(
      `${OUT_OF_RANGE} (Parameter 'startIndex')`,
    );

    expect(caught(() => array().indexOf(1, 1, 5)).message).toBe(
      `${OUT_OF_RANGE} (Parameter 'count')`,
    );

    expect(array().indexOf(3, 1)).toBe(2);
  });

  test("same-instance results", () => {
    const original = array();

    expect(original.addRange([])).toBe(original);
    expect(original.addRange([4])).not.toBe(original);
    expect(original.remove(9)).toBe(original);
    expect(original.removeAll((v) => v > 9)).toBe(original);
    expect(original.removeRange(1, 0)).toBe(original);
    expect(original.clear()).toBe(ImmutableArray.empty());
    expect(ImmutableArray.from(original)).toBe(original);
  });

  test("from copies anything but an ImmutableArray; of takes ownership", () => {
    const source = [1, 2];
    const list = ImmutableList.of([1, 2]);
    const copied = ImmutableArray.from(source);

    source[0] = 9;

    expect([...copied]).toEqual([1, 2]);
    expect(ImmutableArray.from(list)).toBeInstanceOf(ImmutableArray);
    expect(ImmutableArray.of(source)._items).toBe(source);
    expect(() => ImmutableArray.from(null)).toThrow("(Parameter 'items')");
  });

  test("fromRange validates as ImmutableArray.Create(items, start, length), then copies", () => {
    const source = [1, 2, 3];
    const range = ImmutableArray.fromRange(source, 1, 2);

    source[1] = 9;

    expect([...range]).toEqual([2, 3]);
    expect(ImmutableArray.fromRange(source, 3, 0).count).toBe(0);

    expect(caught(() => ImmutableArray.fromRange(null, 0, 0)).message).toBe(
      "Value cannot be null. (Parameter 'items')",
    );

    for (const [start, length, paramName] of [
      [-1, 1, "start"],
      [4, 0, "start"],
      [-1, -1, "start"],
      [0, -1, "length"],
      [2, 2, "length"],
    ] as const) {
      const error = caught(() => ImmutableArray.fromRange(source, start, length));

      expect(error.name).toBe("ArgumentOutOfRangeException");
      expect(error.message).toBe(`${OUT_OF_RANGE} (Parameter '${paramName}')`);
    }
  });

  test("toString, class literal and descriptors", () => {
    const typed = ImmutableArray.of([1], [Int32Type]);

    expect(String(typed)).toBe("System.Collections.Immutable.ImmutableArray`1[System.Int32]");
    expect(String(ImmutableArray.empty())).toBe("System.Collections.Immutable.ImmutableArray`1[T]");
    expect(Object.keys(typed)).toEqual([]);
    expect(getRuntimeType(typed)).toBe(immutableArrayType(Int32Type));
    expect(getRuntimeType(typed.add(2))).toBe(immutableArrayType(Int32Type));
    expect(ImmutableArray.$runtimeType.test(typed)).toBe(true);
    expect(ImmutableArray.$runtimeType.test(ImmutableList.of([1]))).toBe(false);
  });
});
