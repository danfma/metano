import { expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { ImmutableListBuilder, toImmutableList } from "#/system/collections/immutable-list-builder.ts";
import { ImmutableArrayBuilder, toImmutableArray } from "#/system/collections/immutable-array-builder.ts";
import { ImmutableHashSetBuilder, toImmutableHashSet } from "#/system/collections/immutable-hash-set-builder.ts";
import { ImmutableDictionaryBuilder, toImmutableDictionary } from "#/system/collections/immutable-dictionary-builder.ts";
import { ImmutableHashStorage, ownsTip } from "#/system/collections/immutable-storage.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";

test("empty hash versions never compare invisible keys or acquire an append tip", () => {
  const comparer = { getHashCode: (_: object) => 0, equals: (_: object, __: object): boolean => { throw new Error("Unexpected comparison"); } };
  const storage = new ImmutableHashStorage<object, number>(comparer);
  const map = new ImmutableDictionary(storage, 0, null, undefined);
  const set = ImmutableHashSet.of<object>([], comparer);
  for (let i = 0; i < 100; i++) {
    expect(map.add({}, i).count).toBe(1);
    expect(set.add({}).count).toBe(1);
  }
  expect(storage.entries).toEqual([]);
  expect(set._storage.entries).toEqual([]);
  expect(ownsTip([], 0)).toBe(false);
});

test("model: many persistent branches from every typed Empty preserve all prior versions", () => {
  const list = ImmutableList.empty<number>([Int32Type]);
  const array = ImmutableArray.empty<number>([Int32Type]);
  const set = ImmutableHashSet.empty<number>([Int32Type]);
  const map = ImmutableDictionary.empty<number, number>([Int32Type, Int32Type]);
  const versions = [{ list, array, set, map, model: [] as number[] }];
  let seed = 335;
  for (let step = 0; step < 200; step++) {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    const parent = versions[step % 3 === 0 ? 0 : seed % versions.length]!;
    versions.push({ list: parent.list.add(step), array: parent.array.add(step), set: parent.set.add(step), map: parent.map.add(step, step), model: [...parent.model, step] });
    for (const version of versions) {
      expect([...version.list]).toEqual(version.model);
      expect([...version.array]).toEqual(version.model);
      expect([...version.set]).toEqual(version.model);
      expect([...version.map]).toEqual(version.model.map(value => [value, value]));
    }
  }
  expect(list._items).toEqual([]);
  expect(array._items).toEqual([]);
  expect(set._storage.entries).toEqual([]);
  expect(map._storage.entries).toEqual([]);
});

test("builders own independent storage and cleared keyed snapshots are noncanonical", () => {
  const list = ImmutableList.empty<number>([Int32Type]);
  const array = ImmutableArray.empty<number>([Int32Type]);
  const set = ImmutableHashSet.empty<number>([Int32Type]);
  const map = ImmutableDictionary.empty<number, number>([Int32Type, Int32Type]);
  for (let branch = 0; branch < 50; branch++) {
    const lb = new ImmutableListBuilder(list, [Int32Type]);
    const ab = new ImmutableArrayBuilder(array, [Int32Type]);
    const sb = new ImmutableHashSetBuilder(set, null, [Int32Type]);
    const db = new ImmutableDictionaryBuilder(map, null, null, [Int32Type, Int32Type]);
    lb.add(branch); ab.add(branch); sb.add(branch); db.add(branch, branch);
    const snapshots = [toImmutableList(lb), toImmutableArray(ab), toImmutableHashSet(sb), toImmutableDictionary(db)];
    lb.clear(); ab.clear(); sb.clear(); db.clear();
    expect(snapshots.map(value => value.count)).toEqual([1, 1, 1, 1]);
    expect(toImmutableHashSet(sb)).not.toBe(set);
    expect(toImmutableDictionary(db)).not.toBe(map);
    expect([list.count, array.count, set.count, map.count]).toEqual([0, 0, 0, 0]);
  }
});

test("R4: unslotted empty factories share the base-compatible untyped singleton", () => {
  expect(ImmutableList.from<number>([])).toBe(ImmutableList.from<string>([]));
  expect(ImmutableHashSet.from<number>([])).toBe(ImmutableHashSet.from<string>([]));
  expect(ImmutableDictionary.create<number, number>(null)).toBe(ImmutableDictionary.create<string, number>(null));
});

test("collision model: independent and descendant dictionary/set branches remain visible only in their version", () => {
  const comparer = { equals: (a: number, b: number) => a === b, getHashCode: (_: number) => 0 };
  const map = ImmutableDictionary.create<number, number>(comparer, comparer, [Int32Type, Int32Type]);
  const set = ImmutableHashSet.of<number>([], comparer, [Int32Type]);
  const versions = [{ map, set, model: [] as number[] }];
  for (let step = 0; step < 100; step++) {
    const parent = versions[step % 4 === 0 ? 0 : step % versions.length]!;
    versions.push({ map: parent.map.add(step, step), set: parent.set.add(step), model: [...parent.model, step] });
    for (const version of versions) {
      expect([...version.map]).toEqual(version.model.map(value => [value, value]));
      expect([...version.set]).toEqual(version.model);
      for (const value of [0, step, step + 1]) {
        expect(version.map.containsKey(value)).toBe(version.model.includes(value));
        expect(version.set.contains(value)).toBe(version.model.includes(value));
      }
    }
  }
  const sb = new ImmutableHashSetBuilder(set.add(1));
  const db = new ImmutableDictionaryBuilder(map.add(1, 1));
  sb.clear(); db.clear();
  expect(toImmutableHashSet(sb).keyComparer).toBe(comparer);
  expect(toImmutableDictionary(db).keyComparer).toBe(comparer);
  expect(toImmutableDictionary(db).valueComparer).toBe(comparer);
});
