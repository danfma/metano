import { expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { StringType, Int32Type } from "#/system/reflection/primitive-types.ts";
import { UnknownType } from "#/system/reflection/well-known-types.ts";
import { immutableListType, immutableArrayType, immutableHashSetType, immutableDictionaryType } from "#/system/collections/descriptors.ts";

test("unresolved destinations retain source identity and reject unrelated closed tests", () => {
  const list = ImmutableList.of(["x"], [StringType]);
  const array = ImmutableArray.of(["x"], [StringType]);
  const set = ImmutableHashSet.of(["x"], null, [StringType]);
  const map = ImmutableDictionary.create<string, string>(null, null, [StringType, StringType]).add("key", "x");
  for (const destination of [undefined, null, [UnknownType]]) {
    expect(ImmutableList.from(list, destination)).toBe(list);
    expect(ImmutableArray.from(array, destination)).toBe(array);
    expect(ImmutableHashSet.from(set, null, destination)).toBe(set);
    expect(immutableListType(Int32Type).test(ImmutableList.from(list, destination))).toBe(false);
    expect(immutableArrayType(Int32Type).test(ImmutableArray.from(array, destination))).toBe(false);
    expect(immutableHashSetType(Int32Type).test(ImmutableHashSet.from(set, null, destination))).toBe(false);
  }
  for (const destination of [undefined, null, [UnknownType, UnknownType], [StringType, UnknownType]]) {
    const result = ImmutableDictionary.from(map, null, null, destination);
    expect(result).toBe(map);
    expect(immutableDictionaryType(StringType, Int32Type).test(result)).toBe(false);
  }
});

test("comparer-forced copies resolve open arguments from source rather than erasing them", () => {
  const comparer = { equals: (a: string, b: string) => a === b, getHashCode: (_: string) => 0 };
  const set = ImmutableHashSet.of(["x"], null, [StringType]);
  const map = ImmutableDictionary.create<string, string>(null, null, [StringType, StringType]).add("key", "x");
  const copiedSet = ImmutableHashSet.from(set, comparer, [UnknownType]);
  const copiedMap = ImmutableDictionary.from(map, comparer, comparer, [StringType, UnknownType]);
  expect(copiedSet).not.toBe(set);
  expect(copiedMap).not.toBe(map);
  expect(copiedSet.$typeArguments$ImmutableHashSet).toEqual([StringType]);
  expect(copiedMap.$typeArguments$ImmutableDictionary).toEqual([StringType, StringType]);
  expect(copiedSet.keyComparer).toBe(comparer);
  expect(copiedMap.valueComparer).toBe(comparer);
  expect(immutableHashSetType(Int32Type).test(copiedSet)).toBe(false);
  expect(immutableDictionaryType(StringType, Int32Type).test(copiedMap)).toBe(false);
});
