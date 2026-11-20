import { expect, test } from "bun:test";
import { canReuseCollectionTypeArguments, collectionCopyTypeArguments } from "#/system/collections/close-collection.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { StringType, Int32Type } from "#/system/reflection/primitive-types.ts";
import { UnknownType } from "#/system/reflection/well-known-types.ts";
import { immutableListType, immutableArrayType, immutableHashSetType, immutableDictionaryType } from "#/system/collections/descriptors.ts";

test("R1-R3: the source/destination knowledge matrix is directional", () => {
  const vectors = [undefined, [UnknownType], [StringType], [Int32Type]];
  for (const source of vectors) {
    for (const destination of vectors) {
      const expected = destination === undefined || destination[0] === UnknownType ||
        (source !== undefined && source[0] !== UnknownType && source[0] === destination[0]);
      expect(canReuseCollectionTypeArguments(source, destination)).toBe(expected);
    }
  }
  expect(canReuseCollectionTypeArguments([StringType, UnknownType], [StringType, Int32Type])).toBe(false);
  expect(canReuseCollectionTypeArguments([StringType, StringType], [Int32Type, UnknownType])).toBe(true);
  expect(collectionCopyTypeArguments([UnknownType], [StringType])).toEqual([StringType]);
  expect(collectionCopyTypeArguments([StringType, StringType], [Int32Type, UnknownType])).toEqual([StringType, StringType]);
});

test("R2: known destinations replace unknown and absent slots in all immutable families", () => {
  for (const sourceSlots of [undefined, [UnknownType]]) {
    const list = ImmutableList.of(["x"], sourceSlots);
    const array = ImmutableArray.of(["x"], sourceSlots);
    const set = ImmutableHashSet.of(["x"], null, sourceSlots);
    const map = ImmutableDictionary.create<string, string>(null, null, sourceSlots?.concat(sourceSlots)).add("k", "x");
    const l = ImmutableList.from(list, [StringType]);
    const a = ImmutableArray.from(array, [StringType]);
    const s = ImmutableHashSet.from(set, null, [StringType]);
    const m = ImmutableDictionary.from(map, null, null, [StringType, StringType]);
    expect(l).not.toBe(list); expect(a).not.toBe(array); expect(s).not.toBe(set); expect(m).not.toBe(map);
    expect([...l]).toEqual(["x"]); expect([...a]).toEqual(["x"]); expect([...s]).toEqual(["x"]); expect([...m]).toEqual([["k", "x"]]);
    expect(immutableListType(StringType).test(l)).toBe(true);
    expect(immutableArrayType(StringType).test(a)).toBe(true);
    expect(immutableHashSetType(StringType).test(s)).toBe(true);
    expect(immutableDictionaryType(StringType, StringType).test(m)).toBe(true);
    expect(immutableListType(Int32Type).test(l)).toBe(false);
    expect(immutableArrayType(Int32Type).test(a)).toBe(false);
    expect(immutableHashSetType(Int32Type).test(s)).toBe(false);
    expect(immutableDictionaryType(StringType, Int32Type).test(m)).toBe(false);
  }
});

test("R4/R5: unslotted factory empties are singletons that never own tips", () => {
  const list = ImmutableList.from<number>([]);
  const set = ImmutableHashSet.from<number>([]);
  const map = ImmutableDictionary.create<number, number>(null);
  for (let branch = 0; branch < 100; branch++) {
    expect(ImmutableList.from<number>([])).toBe(list);
    expect(ImmutableHashSet.from<number>([])).toBe(set);
    expect(ImmutableDictionary.create<number, number>(null)).toBe(map);
    expect([...list.add(branch)]).toEqual([branch]);
    expect([...set.add(branch)]).toEqual([branch]);
    expect([...map.add(branch, branch)]).toEqual([[branch, branch]]);
  }
  expect(list._items).toEqual([]); expect(set._storage.entries).toEqual([]); expect(map._storage.entries).toEqual([]);
});
