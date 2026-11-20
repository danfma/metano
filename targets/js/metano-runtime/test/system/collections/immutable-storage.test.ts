import { describe, expect, test } from "bun:test";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { type ModelSubject, runPersistentModel } from "./model/persistent-model.ts";
import { createSeededRandom, modelSeeds } from "./model/seeded-random.ts";

// W4-11 condition 2: the immutable classes' tip-append sharing, checked with
// many live versions and branches from old ones against a plain-copy reference.

/** A version of either list-shaped class over `ImmutableListCore`. */
type ListShaped = ImmutableList<number> | ImmutableArray<number>;

/** The model subject of a list-shaped class: both share every member the model drives. */
function listShapedSubject(name: string, empty: () => ListShaped): ModelSubject<ListShaped> {
  return {
    name,
    shape: "list",
    empty,
    apply(version, step) {
      switch (step.op) {
        case "add":
          return version.add(step.value);
        case "addRange":
          return version.addRange(step.items);
        case "remove":
          return version.remove(step.value);
        case "setItem":
          return version.setItem(step.value, step.item);
        case "clear":
          return version.clear();
      }
    },
    count: (version) => version.count,
    contents: (version) => [...version],
    contains: (version, value) => version.contains(value),
  };
}

const listSubject = listShapedSubject("ImmutableList", () => ImmutableList.empty<number>());
const arraySubject = listShapedSubject("ImmutableArray", () => ImmutableArray.empty<number>());

const setSubject: ModelSubject<ImmutableHashSet<number>> = {
  name: "ImmutableHashSet",
  shape: "set",
  empty: () => ImmutableHashSet.empty<number>(),
  apply(version, step) {
    switch (step.op) {
      case "add":
      case "setItem":
        return version.add(step.value);
      case "addRange":
        return version.union(step.items);
      case "remove":
        return version.remove(step.value);
      case "clear":
        return version.clear();
    }
  },
  count: (version) => version.count,
  contents: (version) => [...version],
  contains: (version, value) => version.contains(value),
  tryGetValue: (version, value, missing) => version.tryGetValue(value, missing),
};

const dictionarySubject: ModelSubject<ImmutableDictionary<number, number>> = {
  name: "ImmutableDictionary",
  shape: "dictionary",
  empty: () => ImmutableDictionary.empty<number, number>(),
  apply(version, step) {
    switch (step.op) {
      case "add":
        return version.add(step.value, step.item);
      case "addRange":
        return version.setItems(step.items.map((key) => [key, 0] as const));
      case "setItem":
        return version.setItem(step.value, step.item);
      case "remove":
        return version.remove(step.value);
      case "clear":
        return version.clear();
    }
  },
  count: (version) => version.count,
  contents: (version) => [...version].map(([key, value]) => [key, value]),
  contains: (version, value) => version.containsKey(value),
  tryGetValue: (version, value, missing) => version.tryGetValue(value, missing),
};

const options = { steps: 4000, maxVersions: 32 };

describe("immutable classes against the plain-copy model (W4-11)", () => {
  for (const seed of modelSeeds()) {
    test(`ImmutableList (seed ${seed})`, () => {
      runPersistentModel(listSubject, createSeededRandom(seed), options);
    });

    test(`ImmutableArray (seed ${seed})`, () => {
      runPersistentModel(arraySubject, createSeededRandom(seed), options);
    });

    test(`ImmutableHashSet (seed ${seed})`, () => {
      runPersistentModel(setSubject, createSeededRandom(seed), options);
    });

    test(`ImmutableDictionary (seed ${seed})`, () => {
      runPersistentModel(dictionarySubject, createSeededRandom(seed), options);
    });
  }

  test("the driver branches: an Add on an old version after a newer Add", () => {
    const v1 = ImmutableList.of([1]);
    const v2 = v1.add(2);
    const v3 = v1.add(3);

    expect([...v1]).toEqual([1]);
    expect([...v2]).toEqual([1, 2]);
    expect([...v3]).toEqual([1, 3]);
    expect(v2._items).toBe(v1._items);
    expect(v3._items).not.toBe(v1._items);

    const s1 = ImmutableHashSet.of([1]);
    const s2 = s1.add(2);
    const s3 = s1.add(3);

    expect([s1.contains(2), s1.contains(3), s2.contains(3), s3.contains(2)]).toEqual([
      false,
      false,
      false,
      false,
    ]);

    expect([...s3]).toEqual([1, 3]);
  });
});
