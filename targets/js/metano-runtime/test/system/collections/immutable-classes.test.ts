import { describe, expect, test } from "bun:test";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";

import {
  ImmutableDictionaryBuilder,
  toImmutableDictionary,
} from "#/system/collections/immutable-dictionary-builder.ts";

import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";

import {
  ImmutableHashSetBuilder,
  toImmutableHashSet,
} from "#/system/collections/immutable-hash-set-builder.ts";

import { ImmutableList } from "#/system/collections/immutable-list.ts";

import {
  ImmutableListBuilder,
  toImmutableList,
} from "#/system/collections/immutable-list-builder.ts";

import { List } from "#/system/collections/list.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

// Messages and same-instance results read from .NET 10 (2026-11-19).
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";
const MODIFIED = "Collection was modified; enumeration operation may not execute.";

const ignoreCase = {
  equals: (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value: string): number => value.toLowerCase().length,
};

describe("ImmutableList", () => {
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
});

describe("ImmutableHashSet", () => {
  test("set operations and same-instance results", () => {
    const set = ImmutableHashSet.of([1, 2]);

    expect(set.add(1)).toBe(set);
    expect(set.remove(9)).toBe(set);
    expect(set.union([1])).toBe(set);
    expect(set.except([9])).toBe(set);
    expect(set.intersect([1, 2, 3])).not.toBe(set);
    expect(set.symmetricExcept([])).not.toBe(set);
    expect(set.clear()).toBe(ImmutableHashSet.empty());
    expect(set.withComparer(null)).toBe(set);
    expect(ImmutableHashSet.from(set)).toBe(set);
    expect([...set.union([3, 1])]).toEqual([1, 2, 3]);
    expect([...set.intersect([2, 9])]).toEqual([2]);
    expect([...set.except([1])]).toEqual([2]);
    expect([...set.symmetricExcept([2, 3])]).toEqual([1, 3]);
    expect(set.setEquals([2, 1, 1])).toBe(true);
    expect(set.isProperSubsetOf([1, 2, 3])).toBe(true);
  });

  test("the comparer decides equality; tryGetValue returns the stored element", () => {
    const set = ImmutableHashSet.of(["Abc"], ignoreCase);

    expect(set.contains("ABC")).toBe(true);
    expect(set.tryGetValue("ABC", "")).toEqual([true, "Abc"]);
    expect(set.tryGetValue("x", "")).toEqual([false, ""]);
    expect(set.keyComparer).toBe(ignoreCase);
    expect(set.withComparer(null).count).toBe(1);
  });

  test("null elements are allowed; a null other throws", () => {
    expect(ImmutableHashSet.of<string | null>(["a", null]).count).toBe(2);
    expect(() => ImmutableHashSet.of([1]).union(null as never)).toThrow("(Parameter 'other')");
  });

  test("toJSON and toString", () => {
    const set = ImmutableHashSet.of([1], null, [Int32Type]);

    expect(JSON.stringify(set)).toBe("[1]");
    expect(String(set)).toBe("System.Collections.Immutable.ImmutableHashSet`1[System.Int32]");
  });
});

describe("ImmutableDictionary", () => {
  const dictionary = (): ImmutableDictionary<string, number> =>
    ImmutableDictionary.empty<string, number>().add("a", 1);

  test("add, setItem and remove with .NET's same-instance results", () => {
    const d = dictionary();

    expect(d.add("a", 1)).toBe(d);

    expect(() => d.add("a", 2)).toThrow(
      "An element with the same key but a different value already exists. Key: 'a'",
    );

    expect(d.setItem("a", 1)).toBe(d);
    expect(d.setItems([["a", 1]])).toBe(d);
    expect(d.addRange([["a", 1]])).toBe(d);
    expect(d.remove("z")).toBe(d);
    expect(d.removeRange(["z"])).toBe(d);
    expect(d.withComparers(null, null)).toBe(d);
    expect(d.clear()).toBe(ImmutableDictionary.empty());
    expect(d.setItem("b", 2).count).toBe(2);
    expect([...d.setItem("a", 5)]).toEqual([["a", 5]]);
    expect(d.get("a")).toBe(1);
  });

  test("reads", () => {
    const d = dictionary().setItem("b", 2);

    expect(d.contains(["a", 1])).toBe(true);
    expect(d.contains(["a", 2])).toBe(false);
    expect(d.containsValue(2)).toBe(true);
    expect(d.tryGetValue("z", 0)).toEqual([false, 0]);
    expect(d.getValueOrDefault("z", -1)).toBe(-1);
    expect([...d.keys]).toEqual(["a", "b"]);
    expect([...d.values]).toEqual([1, 2]);
    expect(() => d.get("z")).toThrow("The given key 'z' was not present in the dictionary.");
  });

  test("null keys throw as .NET", () => {
    const d = ImmutableDictionary.empty<string | null, number>();

    for (const action of [
      () => d.add(null, 1),
      () => d.get(null),
      () => d.containsKey(null),
      () => d.remove(null),
    ]) {
      expect(action).toThrow("Value cannot be null. (Parameter 'key')");
    }

    expect(() => d.tryGetKey(null, "")).toThrow("(Parameter 'equalKey')");
  });

  test("comparers", () => {
    const d = ImmutableDictionary.create<string, number>(ignoreCase).add("Abc", 1);

    expect(d.containsKey("ABC")).toBe(true);
    expect(d.tryGetKey("ABC", "")).toEqual([true, "Abc"]);
    expect(d.withComparers(null).containsKey("ABC")).toBe(false);

    expect(() =>
      ImmutableDictionary.from(
        [
          ["a", 1],
          ["A", 2],
        ],
        ignoreCase,
      ),
    ).toThrow("Key: 'A'");

    expect(
      ImmutableDictionary.fromSelectors(
        [1, 2],
        (v) => `${v}`,
        (v) => v * 10,
      ).get("2"),
    ).toBe(20);
  });

  test("toJSON and toString", () => {
    const d = ImmutableDictionary.create<string, number>(null, null, [StringType, Int32Type]).add(
      "a",
      1,
    );

    expect(JSON.stringify(d)).toBe('{"a":1}');

    expect(String(d)).toBe(
      "System.Collections.Immutable.ImmutableDictionary`2[System.String,System.Int32]",
    );
  });
});

describe("builders (W4-11 condition 4: never alias)", () => {
  test("ToImmutable, then a further builder mutation changes no immutable value", () => {
    const listBuilder = new ImmutableListBuilder<number>();

    listBuilder.add(1);

    const list = toImmutableList(listBuilder);

    listBuilder.add(2);
    listBuilder.set(0, 9);

    expect([...list]).toEqual([1]);
    expect(list.add(3)._items).not.toBe(listBuilder._list._items);

    const setBuilder = new ImmutableHashSetBuilder<number>();

    setBuilder.add(1);

    const set = toImmutableHashSet(setBuilder);

    setBuilder.add(2);
    setBuilder.remove(1);

    expect([...set]).toEqual([1]);

    const dictionaryBuilder = new ImmutableDictionaryBuilder<string, number>();

    dictionaryBuilder.set("a", 1);

    const dictionary = toImmutableDictionary(dictionaryBuilder);

    dictionaryBuilder.set("a", 2);
    dictionaryBuilder.set("b", 3);

    expect([...dictionary]).toEqual([["a", 1]]);
  });

  test("ToBuilder copies, and the immutable source is unchanged by the builder", () => {
    const list = ImmutableList.of([1, 2]);
    const builder = new ImmutableListBuilder(list);

    builder.add(3);

    expect([...list]).toEqual([1, 2]);
    expect([...toImmutableList(builder)]).toEqual([1, 2, 3]);

    const set = ImmutableHashSet.of(["Abc"], ignoreCase);

    expect(new ImmutableHashSetBuilder(set).keyComparer).toBe(ignoreCase);
  });

  test("a builder invalidates its enumeration on every mutation, a removal included", () => {
    const listBuilder = new ImmutableListBuilder([1, 2]);
    const setBuilder = new ImmutableHashSetBuilder([1, 2]);

    const dictionaryBuilder = new ImmutableDictionaryBuilder<number, number>([
      [1, 1],
      [2, 2],
    ]);

    expect(() => {
      for (const _ of listBuilder) {
        listBuilder.add(3);
      }
    }).toThrow(MODIFIED);

    expect(() => {
      for (const _ of setBuilder) {
        setBuilder.remove(2);
      }
    }).toThrow(MODIFIED);

    expect(() => {
      for (const _ of dictionaryBuilder) {
        dictionaryBuilder.remove(2);
      }
    }).toThrow(MODIFIED);
  });

  test("a builder's index checks use .NET's builder text", () => {
    const builder = new ImmutableListBuilder<number>();

    expect(() => builder.get(0)).toThrow(`${OUT_OF_RANGE} (Parameter 'index')`);
    expect(builder).not.toBeInstanceOf(List);
    expect(String(builder)).toBe("System.Collections.Immutable.ImmutableList`1+Builder[T]");
  });
});
