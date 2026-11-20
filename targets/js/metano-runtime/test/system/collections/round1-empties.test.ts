import { describe, expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { immutableListType, immutableHashSetType, immutableDictionaryType } from "#/system/collections/descriptors.ts";

describe("round 1 canonical typed empties", () => {
  test("list Clear and removals return Empty for exactly the source descriptor", () => {
    const source = ImmutableList.of([1], [Int32Type]);
    const empty = ImmutableList.empty([Int32Type]);
    const results = [source.clear(), source.remove(1), source.removeAt(0), source.removeRange(0, 1), source.removeAll(() => true), source.getRange(0, 0)];
    for (const result of results) {
      expect(result).toBe(empty);
      expect(getRuntimeType(result)).toBe(immutableListType(Int32Type));
      expect(immutableListType(StringType).test(result)).toBe(false);
    }
    expect(ImmutableList.of([], [Int32Type])).toBe(empty);
    expect(ImmutableList.from([], [Int32Type])).toBe(empty);
    expect(source.count).toBe(1);
  });

  test("set Clear is canonical; removal empties keep their slots and .NET identities", () => {
    const source = ImmutableHashSet.of([1], null, [Int32Type]);
    const empty = ImmutableHashSet.empty([Int32Type]);
    expect(source.clear()).toBe(empty);
    for (const result of [source.remove(1), source.except([1]), source.intersect([])]) {
      // .NET's removal/intersection empties are new versions, unlike nonempty Clear.
      expect(result).not.toBe(empty);
      expect(getRuntimeType(result)).toBe(immutableHashSetType(Int32Type));
      expect(immutableHashSetType(StringType).test(result)).toBe(false);
    }
    expect(ImmutableHashSet.of([], null, [Int32Type])).toBe(empty);
  });

  test("default-comparer dictionary transitions return the canonical typed Empty", () => {
    const source = ImmutableDictionary.create<number, number>(null, null, [Int32Type, Int32Type]).add(1, 1);
    const empty = ImmutableDictionary.empty([Int32Type, Int32Type]);
    for (const result of [source.clear(), source.remove(1), source.removeRange([1])]) {
      expect(result).toBe(empty);
      expect(getRuntimeType(result)).toBe(immutableDictionaryType(Int32Type, Int32Type));
      expect(immutableDictionaryType(Int32Type, StringType).test(result)).toBe(false);
    }
    expect(ImmutableDictionary.create(null, null, [Int32Type, Int32Type])).toBe(empty);
  });

  test("clearing custom-comparer sets and dictionaries preserves comparers and slots", () => {
    const comparer = { equals: (a: number, b: number) => a === b, getHashCode: (value: number) => value };
    const set = ImmutableHashSet.of([1], comparer, [Int32Type]).clear();
    const map = ImmutableDictionary.create<number, number>(comparer, comparer, [Int32Type, Int32Type]).add(1, 1).clear();
    expect(set.keyComparer).toBe(comparer);
    expect(map.keyComparer).toBe(comparer);
    expect(map.valueComparer).toBe(comparer);
    expect(set).not.toBe(ImmutableHashSet.empty([Int32Type]));
    expect(map).not.toBe(ImmutableDictionary.empty([Int32Type, Int32Type]));
    expect(getRuntimeType(set)).toBe(immutableHashSetType(Int32Type));
    expect(getRuntimeType(map)).toBe(immutableDictionaryType(Int32Type, Int32Type));
  });
});
