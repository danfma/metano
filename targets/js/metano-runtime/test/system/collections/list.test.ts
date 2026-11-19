import { describe, expect, test } from "bun:test";
import { createComparer } from "#/system/collections/default-comparer.ts";
import { List } from "#/system/collections/list.ts";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { ArgumentNullException } from "#/system/exceptions/argument-null-exception.ts";

import {
  ArgumentOutOfRangeException,
} from "#/system/exceptions/argument-out-of-range-exception.ts";

import { InvalidOperationException } from "#/system/exceptions/invalid-operation-exception.ts";
import { NotSupportedException } from "#/system/exceptions/not-supported-exception.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

/** A record-like value: equal by `x`, as a lowered C# record is. */
class Point {
  constructor(readonly x: number) {}

  equals(other: unknown): boolean {
    return other instanceof Point && other.x === this.x;
  }

  hashCode(): number {
    return this.x;
  }
}

const INDEX_MESSAGE =
  "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')";

const MODIFIED_MESSAGE = "Collection was modified; enumeration operation may not execute.";

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("List construction", () => {
  test("of takes ownership of the array", () => {
    const items = [1, 2];
    const list = List.of(items);

    expect(list.asJsArray()).toBe(items);
    expect(list.count).toBe(2);
  });

  test("the constructor copies; null and undefined are empty", () => {
    const source = [1, 2];
    const list = new List(source);

    expect(list.asJsArray()).not.toBe(source);
    expect(new List<number>(null).count).toBe(0);
    expect(new List<number>().count).toBe(0);
  });

  test("from copies and rejects null like the C# copy constructor", () => {
    expect(List.from(new Set([3, 4])).toArray()).toEqual([3, 4]);

    const error = thrown(() => List.from(null as unknown as number[]));

    expect(error).toBeInstanceOf(ArgumentNullException);
    expect(error.message).toBe("Value cannot be null. (Parameter 'collection')");
  });

  test("withCapacity validates its argument", () => {
    expect(List.withCapacity<number>(10).count).toBe(0);

    const error = thrown(() => List.withCapacity(-1));

    expect(error).toBeInstanceOf(ArgumentOutOfRangeException);
    expect(error.message).toBe("Non-negative number required. (Parameter 'capacity')");
  });

  test("the type-argument slot is defined, undefined without descriptors", () => {
    expect(Object.hasOwn(List.of([1]), "$typeArguments$List")).toBe(true);
    expect(List.of([1]).$typeArguments$List).toBeUndefined();
    expect(List.of([1], [Int32Type]).$typeArguments$List).toEqual([Int32Type]);
  });

  test("storage is invisible to keys, spread, JSON and Array.isArray", () => {
    const list = List.of([1, 2]);

    expect(Object.keys(list)).toEqual([]);
    expect({ ...list }).toEqual({});
    expect(JSON.stringify({ list })).toBe('{"list":[1,2]}');
    expect(Array.isArray(list)).toBe(false);
  });
});

describe("List indexer", () => {
  test("get / set / fromEnd within range", () => {
    const list = List.of([1, 2, 3]);

    list.set(1, 20);

    expect(list.get(1)).toBe(20);
    expect(list.fromEnd(1)).toBe(3);
  });

  test.each([-1, 3])("get(%p) throws ArgumentOutOfRangeException", (index) => {
    const error = thrown(() => List.of([1, 2, 3]).get(index));

    expect(error).toBeInstanceOf(ArgumentOutOfRangeException);
    expect(error.message).toBe(INDEX_MESSAGE);
  });

  test("set, fromEnd(0) and fromEnd(count + 1) throw", () => {
    const list = List.of([1, 2, 3]);

    expect(thrown(() => list.set(3, 0)).message).toBe(INDEX_MESSAGE);
    expect(thrown(() => list.fromEnd(0)).message).toBe(INDEX_MESSAGE);
    expect(thrown(() => list.fromEnd(4)).message).toBe(INDEX_MESSAGE);
  });

  test("update and updateReturningOld read, apply and write once", () => {
    const list = List.of([5]);

    expect(list.update(0, (v) => v + 1)).toBe(6);
    expect(list.updateReturningOld(0, (v) => v + 1)).toBe(6);
    expect(list.get(0)).toBe(7);
    expect(thrown(() => list.update(1, (v) => v)).message).toBe(INDEX_MESSAGE);
  });
});

describe("List mutators", () => {
  test("add / addRange / insert / insertRange", () => {
    const list = List.of([1]);

    list.add(2);
    list.addRange([3, 4]);
    list.insert(0, 0);
    list.insertRange(5, [5, 6]);

    expect(list.toArray()).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  test("addRange(this) appends a snapshot", () => {
    const list = List.of([1, 2, 3]);

    list.addRange(list);

    expect(list.toArray()).toEqual([1, 2, 3, 1, 2, 3]);
  });

  test("insert and insertRange messages", () => {
    const list = List.of([1]);

    expect(thrown(() => list.insert(2, 0)).message).toBe(
      "Index must be within the bounds of the List. (Parameter 'index')",
    );

    expect(thrown(() => list.insertRange(2, [0])).message).toBe(
      "Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'index')",
    );

    expect(thrown(() => list.addRange(null as unknown as number[])).message).toBe(
      "Value cannot be null. (Parameter 'collection')",
    );
  });

  test("remove / removeAt / removeRange / removeAll / clear", () => {
    const list = List.of([1, 2, 3, 4, 5, 6]);

    expect(list.remove(2)).toBe(true);
    expect(list.remove(99)).toBe(false);
    list.removeAt(0);
    list.removeRange(0, 1);
    expect(list.removeAll((x) => x > 4)).toBe(2);
    expect(list.toArray()).toEqual([4]);
    list.clear();
    expect(list.count).toBe(0);
  });

  test("removeAt / getRange / removeRange / slice messages", () => {
    const list = List.of([1, 2, 3]);

    expect(thrown(() => list.removeAt(3)).message).toBe(INDEX_MESSAGE);

    expect(thrown(() => list.getRange(-1, 1)).message).toBe(
      "Non-negative number required. (Parameter 'index')",
    );

    expect(thrown(() => list.getRange(0, -1)).message).toBe(
      "Non-negative number required. (Parameter 'count')",
    );

    const range = thrown(() => list.removeRange(2, 5));

    expect(range).toBeInstanceOf(ArgumentException);

    expect(range.message).toBe(
      "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.",
    );

    expect(thrown(() => list.slice(2, 1)).message).toBe(
      "Non-negative number required. (Parameter 'count')",
    );
  });

  test("slice and getRange build new lists and keep the type arguments", () => {
    const list = List.of([1, 2, 3, 4], [Int32Type]);

    expect(list.slice(1).toArray()).toEqual([2, 3, 4]);
    expect(list.slice(1, 3).toArray()).toEqual([2, 3]);
    expect(list.getRange(1, 2).$typeArguments$List).toEqual([Int32Type]);
  });
});

describe("List searches", () => {
  test("contains / indexOf / lastIndexOf use elementEquals", () => {
    const list = List.of([new Point(1), new Point(2), new Point(1)]);

    expect(list.contains(new Point(2))).toBe(true);
    expect(list.indexOf(new Point(1))).toBe(0);
    expect(list.indexOf(new Point(1), 1)).toBe(2);
    expect(list.lastIndexOf(new Point(1))).toBe(2);
    expect(List.of([Number.NaN]).indexOf(Number.NaN)).toBe(0);
  });

  test("an array element compares by reference", () => {
    const element = [1];

    expect(List.of([element]).contains([1])).toBe(false);
    expect(List.of([element]).contains(element)).toBe(true);
  });

  test("indexOf(item, index) past the end throws", () => {
    expect(thrown(() => List.of([1]).indexOf(1, 5)).message).toBe(
      "Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'index')",
    );
  });

  test("find / findLast return the C# default on a miss", () => {
    const list = List.of([1, 2, 3]);

    expect(list.find((x) => x > 1)).toBe(2);
    expect(list.find((x) => x > 10, 0)).toBe(0);
    expect(list.find((x) => x > 10)).toBeNull();
    expect(list.findLast((x) => x < 3, 0)).toBe(2);
    expect(list.findIndex((x) => x === 3)).toBe(2);
    expect(list.findLastIndex((x) => x === 9)).toBe(-1);
  });

  test("findAll / exists / trueForAll / convertAll", () => {
    const list = List.of([1, 2, 3]);

    expect(list.findAll((x) => x !== 2).toArray()).toEqual([1, 3]);
    expect(list.exists((x) => x === 3)).toBe(true);
    expect(list.trueForAll((x) => x > 0)).toBe(true);
    expect(list.convertAll((x) => `${x}`).toArray()).toEqual(["1", "2", "3"]);

    expect(thrown(() => list.exists(null as unknown as () => boolean)).message).toBe(
      "Value cannot be null. (Parameter 'match')",
    );
  });

  test("binarySearch returns the index or the complement of the insertion point", () => {
    const list = List.of([1, 3, 5]);

    expect(list.binarySearch(3)).toBe(1);
    expect(list.binarySearch(4)).toBe(-3);
    expect(List.of<number>([]).binarySearch(5)).toBe(-1);

    expect(
      list.binarySearch(
        9,
        createComparer((a: number, b: number) => a - b),
      ),
    ).toBe(-4);
  });
});

describe("List order", () => {
  test("sort with the default comparer, a comparison and an IComparer", () => {
    const list = List.of([3, Number.NaN, 1]);

    list.sort();
    expect(list.toArray()).toEqual([Number.NaN, 1, 3]);

    const numbers = List.of([1, 3, 2]);

    numbers.sort((a, b) => b - a);
    expect(numbers.toArray()).toEqual([3, 2, 1]);

    const strings = List.of(["b", "a", "B", "A"]);

    strings.sort(createComparer((a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0)));
    expect(strings.toArray()).toEqual(["A", "B", "a", "b"]);
  });

  test(
    "sort() without a comparison orders by value, not as strings (2.x sorted [10, 9, 100] as text)",
    () => {
      const numbers = List.of([10, 9, 100, -5, -50, 0.5]);
      const bigints = List.of([10n, 9n, 100n, -5n]);
      const strings = List.of(["b", "10", "9", "a"]);

      numbers.sort();
      bigints.sort();
      strings.sort();

      expect(numbers.toArray()).toEqual([-50, -5, 0.5, 9, 10, 100]);
      expect(bigints.toArray()).toEqual([-5n, 9n, 10n, 100n]);
      expect(strings.toArray()).toEqual(["10", "9", "a", "b"]);
    },
  );

  test("a comparison failure is wrapped as .NET's Array.Sort wraps it", () => {
    const objects = List.of([{}, {}]);
    const error = thrown(() => objects.sort());

    expect(error).toBeInstanceOf(InvalidOperationException);
    expect(error.message).toBe("Failed to compare two elements in the array.");

    expect(error instanceof InvalidOperationException && error.innerException?.message).toBe(
      "At least one object must implement IComparable.",
    );

    expect(() => List.of([{}]).sort()).not.toThrow();
  });

  test("reverse in place", () => {
    const list = List.of([1, 2, 3]);
    const backing = list.asJsArray();

    list.reverse();

    expect(list.asJsArray()).toBe(backing);
    expect(backing).toEqual([3, 2, 1]);
  });
});

describe("List views and copies", () => {
  test("toArray detaches; the views are the backing array", () => {
    const list = List.of([1, 2]);
    const copy = list.toArray();

    copy[0] = 9;

    expect(list.get(0)).toBe(1);
    expect(list.asSpan()).toBe(list.asJsArray());
    expect(list.asReadOnlySpan()).toBe(list.asJsArray());
    list.clear();
    list.add(5);
    expect(list.asJsArray()).toEqual([5]);
  });

  test("copyTo checks the destination", () => {
    const list = List.of([1, 2]);
    const destination = [0, 0, 0];

    list.copyTo(destination, 1);

    expect(destination).toEqual([0, 1, 2]);

    expect(thrown(() => list.copyTo([0], 0)).message).toBe(
      "Destination array was not long enough. Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')",
    );

    expect(thrown(() => list.copyTo([0, 0, 0], -1)).message).toBe(
      "destinationIndex ('-1') must be greater than or equal to '0'. (Parameter 'destinationIndex')\nActual value was -1.",
    );
  });

  test("toString prints .NET's type name", () => {
    expect(List.of([1], [Int32Type]).toString()).toBe(
      "System.Collections.Generic.List`1[System.Int32]",
    );

    expect(List.of([1]).toString()).toBe("System.Collections.Generic.List`1[T]");
  });
});

describe("List enumeration", () => {
  test("iterates in order", () => {
    expect([...List.of([1, 2, 3])]).toEqual([1, 2, 3]);
  });

  test("a mutation during iteration throws on the next step, the final one included", () => {
    const list = List.of([1, 2]);

    const error = thrown(() => {
      for (const item of list) {
        if (item === 2) {
          list.add(3);
        }
      }
    });

    expect(error).toBeInstanceOf(InvalidOperationException);
    expect(error.message).toBe(MODIFIED_MESSAGE);
  });

  test("set, sort, reverse and clear invalidate; no-op range members do not", () => {
    for (const mutate of [
      (l: List<number>) => l.set(0, 5),
      (l: List<number>) => l.sort(),
      (l: List<number>) => l.reverse(),
      (l: List<number>) => List.of<number>([]).clear() ?? l.clear(),
    ]) {
      const list = List.of([1, 2]);

      expect(() => {
        for (const _ of list) mutate(list);
      }).toThrow(MODIFIED_MESSAGE);
    }

    for (const noOp of [
      (l: List<number>) => l.addRange([]),
      (l: List<number>) => l.removeRange(0, 0),
      (l: List<number>) => l.removeAll(() => false),
      (l: List<number>) => l.remove(99),
    ]) {
      const list = List.of([1, 2]);

      expect(() => {
        for (const _ of list) noOp(list);
      }).not.toThrow();
    }
  });

  test("a length change through asJsArray is detected", () => {
    const list = List.of([1, 2]);

    expect(() => {
      for (const _ of list) list.asJsArray().push(3);
    }).toThrow(MODIFIED_MESSAGE);
  });

  test("break after a mutation does not throw", () => {
    const list = List.of([1, 2, 3]);

    expect(() => {
      for (const item of list) {
        list.remove(item);
        break;
      }
    }).not.toThrow();
  });

  test("forEach throws when the action changes the list", () => {
    const list = List.of([1, 2]);

    expect(() => list.forEach(() => list.add(0))).toThrow(MODIFIED_MESSAGE);
  });
});

describe("fixed-size adopted list", () => {
  test("shares the array; writes go through", () => {
    const array = [1, 2];
    const list = List.adopt(array, { fixedSize: true });

    list.set(0, 9);
    list.sort();

    expect(array).toEqual([2, 9]);
    expect(list.isFixedSize).toBe(true);
    expect(list.isReadOnly).toBe(true);
    expect(list).toBeInstanceOf(List);
  });

  test("size changes throw NotSupportedException with .NET's messages", () => {
    const list = List.adopt([1, 2], { fixedSize: true });

    for (const change of [
      () => list.add(3),
      () => list.addRange([3]),
      () => list.insert(0, 3),
      () => list.insertRange(0, [3]),
      () => list.remove(1),
      () => list.removeAt(0),
      () => list.removeRange(0, 1),
      () => list.removeAll(() => true),
    ]) {
      const error = thrown(change);

      expect(error).toBeInstanceOf(NotSupportedException);
      expect(error.message).toBe("Collection was of a fixed size.");
    }

    expect(thrown(() => list.clear()).message).toBe("Collection is read-only.");
  });

  test("writes never invalidate an enumeration, as an array's enumerator", () => {
    const list = List.adopt([1, 2], { fixedSize: true });

    expect(() => {
      for (const _ of list) list.set(0, 5);
    }).not.toThrow();
  });

  test("a growable adopted list shares the array and grows it", () => {
    const array = [1];
    const list = List.adopt(array);

    list.add(2);

    expect(array).toEqual([1, 2]);
    expect(list.isFixedSize).toBe(false);
  });
});

describe("initializer factories", () => {
  test("initAdd after a capacity constructor", () => {
    expect(List.withCapacity<number>(4).initAdd([1, 2]).toArray()).toEqual([1, 2]);
  });
});
