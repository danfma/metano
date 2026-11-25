import { describe, expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

// Messages and same-instance results read from .NET 10 (2026-11-19).
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";

describe("ImmutableList", () => {
  test("lastIndexOf edge ranges follow .NET's check order", () => {
    const list = ImmutableList.of([10, 20, 30]);
    const empty = ImmutableList.empty<number>();

    expect(list.lastIndexOf(20, 5, 1)).toBe(-1);
    expect(list.lastIndexOf(20, 3, 1)).toBe(-1);
    expect(() => list.lastIndexOf(20, 5, 9)).toThrow("(Parameter 'count')");
    expect(() => list.lastIndexOf(20, 0, 2)).toThrow("Value does not fall within");
    expect(() => empty.lastIndexOf(20, -1, 0)).toThrow("(Parameter 'index')");
    expect(() => empty.lastIndexOf(20, 0, -1)).toThrow("(Parameter 'count')");
    expect(empty.lastIndexOf(20)).toBe(-1);
  });

  const list = (): ImmutableList<number> => ImmutableList.of([1, 2, 3]);

  test("mutators return new lists and leave the receiver unchanged", () => {
    const original = list();
    const added = original.add(4);

    expect([...original]).toEqual([1, 2, 3]);
    expect([...added]).toEqual([1, 2, 3, 4]);
    expect([...original.insert(0, 0)]).toEqual([0, 1, 2, 3]);
    expect([...original.insertRange(3, [4, 5])]).toEqual([1, 2, 3, 4, 5]);
    expect([...original.removeAt(1)]).toEqual([1, 3]);
    expect([...original.removeRange(0, 2)]).toEqual([3]);
    expect([...original.removeItems([3, 1])]).toEqual([2]);
    expect([...original.removeAll((v) => v > 1)]).toEqual([1]);
    expect([...original.setItem(0, 9)]).toEqual([9, 2, 3]);
    expect([...original.replace(2, 8)]).toEqual([1, 8, 3]);
    expect([...ImmutableList.of([3, 1, 2]).sort()]).toEqual([1, 2, 3]);
    expect([...original.sort((a, b) => b - a)]).toEqual([3, 2, 1]);
    expect([...original.reverse()]).toEqual([3, 2, 1]);
    expect([...original.getRange(1, 2)]).toEqual([2, 3]);
    expect([...original.slice(1)]).toEqual([2, 3]);
    expect([...original]).toEqual([1, 2, 3]);
  });

  test("same-instance results follow .NET", () => {
    const original = list();

    expect(original.remove(9)).toBe(original);
    expect(original.removeAll((v) => v > 9)).toBe(original);
    expect(original.removeItems([9])).toBe(original);
    expect(original.removeRange(1, 0)).toBe(original);
    expect(ImmutableList.of([1]).reverse().count).toBe(1);
    expect(original.addRange([])).not.toBe(original);
    expect(original.insertRange(1, [])).not.toBe(original);
    expect(original.setItem(0, 1)).not.toBe(original);
    expect(original.sort()).not.toBe(original);
    expect(original.clear()).toBe(ImmutableList.empty());
    expect(ImmutableList.from(original)).toBe(original);
  });

  test("reads and searches", () => {
    const original = ImmutableList.of([1, 2, 1]);

    expect(original.get(1)).toBe(2);
    expect(original.fromEnd(1)).toBe(1);
    expect(original.indexOf(1)).toBe(0);
    expect(original.lastIndexOf(1)).toBe(2);
    expect(ImmutableList.of([1, 2, 3]).indexOf(3, 0, 2)).toBe(-1);
    expect(ImmutableList.of([1, 2, 3]).indexOf(3, 1, 2)).toBe(2);
    expect(original.find((v) => v > 5, 0)).toBe(0);
    expect(original.findLastIndex((v) => v === 1)).toBe(2);
    expect([...original.findAll((v) => v === 1)]).toEqual([1, 1]);
    expect([...original.convertAll((v) => `${v}`)]).toEqual(["1", "2", "1"]);
    expect(ImmutableList.of([1, 3, 5]).binarySearch(4)).toBe(~2);
    expect(original.exists((v) => v === 2)).toBe(true);
    expect(original.trueForAll((v) => v > 0)).toBe(true);
  });

  test("errors carry .NET's texts and parameter names", () => {
    const original = list();

    expect(() => original.get(3)).toThrow(`${OUT_OF_RANGE} (Parameter 'index')`);
    expect(() => original.insert(5, 1)).toThrow("(Parameter 'index')");
    expect(() => original.removeAt(3)).toThrow("(Parameter 'index')");
    expect(() => original.setItem(3, 1)).toThrow("(Parameter 'index')");
    expect(() => original.removeRange(2, 5)).toThrow(`${OUT_OF_RANGE} (Parameter 'count')`);
    expect(() => original.getRange(-1, 1)).toThrow("(Parameter 'index')");
    expect(() => original.indexOf(3, 2, 5)).toThrow("(Parameter 'count')");

    expect(() => original.replace(9, 1)).toThrow(
      "Cannot find the old value (Parameter 'oldValue')",
    );

    expect(() => original.addRange(null as never)).toThrow("(Parameter 'items')");
    expect(() => ImmutableList.from(null)).toThrow("(Parameter 'items')");
    expect(() => original.removeAll(null as never)).toThrow("(Parameter 'match')");
    expect(() => original.copyTo([0])).toThrow(`${OUT_OF_RANGE} (Parameter 'arrayIndex')`);
    expect(() => original.copyTo([0, 0, 0, 0], -1)).toThrow("(Parameter 'arrayIndex')");

    expect(() => ImmutableList.of([{}, {}]).sort()).toThrow(
      "Failed to compare two elements in the array.",
    );

    expect(() => ImmutableList.of([{}, {}]).binarySearch({})).toThrow(
      "At least one object must implement IComparable.",
    );
  });

  test("copyTo, toJSON, toString and invisible storage", () => {
    const original = ImmutableList.of([1, 2], [Int32Type]);
    const target = [0, 0, 0];

    original.copyTo(target, 1);

    expect(target).toEqual([0, 1, 2]);
    expect(JSON.stringify(original.add(3))).toBe("[1,2,3]");
    expect(JSON.stringify(original)).toBe("[1,2]");
    expect(String(original)).toBe("System.Collections.Immutable.ImmutableList`1[System.Int32]");

    expect(String(original.add(3))).toBe(
      "System.Collections.Immutable.ImmutableList`1[System.Int32]",
    );

    expect(Object.keys(original)).toEqual([]);
    expect(ImmutableList.$runtimeType.test(original)).toBe(true);
  });

  test("length and asReadOnlyJsArray respect immutability and tip-append sharing", () => {
    const v1 = ImmutableList.of([1, 2]);
    const view1 = v1.asReadOnlyJsArray();

    expect(v1.length).toBe(2);
    expect(v1.asReadOnlyJsArray()).toBe(view1);
    expect(view1).toEqual([1, 2]);

    const v2 = v1.add(3);

    expect(v2.length).toBe(3);
    expect(view1).toEqual([1, 2]);
    expect(v2.asReadOnlyJsArray()).toEqual([1, 2, 3]);

    const v3 = v2.add(4);

    // v2 is now an older version sharing storage with v3: its view is detached to visible elements.
    expect(v2.asReadOnlyJsArray()).toEqual([1, 2, 3]);
    expect(v3.asReadOnlyJsArray()).toEqual([1, 2, 3, 4]);

    const empty = ImmutableList.empty<number>();
    const emptyView = empty.asReadOnlyJsArray();

    expect(empty.length).toBe(0);
    expect(emptyView).toEqual([]);
    expect(empty.add(1).asReadOnlyJsArray()).toEqual([1]);
    expect(emptyView).toEqual([]);
  });
});
