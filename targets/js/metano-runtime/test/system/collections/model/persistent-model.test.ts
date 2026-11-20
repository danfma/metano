import { describe, expect, test } from "bun:test";
import { type ModelStep, type ModelSubject, runPersistentModel } from "./persistent-model.ts";
import { createSeededRandom, modelSeeds } from "./seeded-random.ts";

// Self-test of the model driver, before the immutable classes exist: it must
// accept a correct copy-everything subject and a correct tip-append subject,
// and reject a subject whose versions alias the shared storage.

class OutOfRange extends Error {
  override readonly name = "ArgumentOutOfRangeException";
}

class DuplicateKey extends Error {
  override readonly name = "ArgumentException";
}

/** A list version over a possibly shared array; it owns the first `count` slots. */
type TipList = { readonly store: number[]; readonly count: number };

/**
 * Tip-append over one array. `checksOwnership: false` is the bug the model
 * must catch: every version appends in place, so an older version's append
 * overwrites what a newer version already sees.
 */
function tipListSubject(checksOwnership: boolean): ModelSubject<TipList> {
  const visible = (version: TipList): number[] => version.store.slice(0, version.count);
  const copied = (values: number[]): TipList => ({ store: values, count: values.length });

  const append = (version: TipList, values: readonly number[]): TipList => {
    const owns = !checksOwnership || version.store.length === version.count;
    const store = owns ? version.store : visible(version);

    store.length = version.count;
    store.push(...values);

    return { store, count: version.count + values.length };
  };

  const apply = (version: TipList, step: ModelStep): TipList => {
    switch (step.op) {
      case "add":
        return append(version, [step.value]);
      case "addRange":
        return append(version, step.items);
      case "remove": {
        const values = visible(version);
        const index = values.indexOf(step.value);

        return index === -1 ? version : copied(values.filter((_, i) => i !== index));
      }

      case "setItem":
        if (step.value >= version.count) {
          throw new OutOfRange();
        }

        return copied(visible(version).map((value, i) => (i === step.value ? step.item : value)));
      case "clear":
        return copied([]);
    }
  };

  return {
    name: checksOwnership ? "tip-append list" : "aliasing list",
    shape: "list",
    empty: () => copied([]),
    apply,
    count: (version) => version.count,
    contents: visible,
    contains: (version, value) => visible(version).includes(value),
  };
}

/** A plain-copy set over insertion-ordered arrays. */
const copySetSubject: ModelSubject<readonly number[]> = {
  name: "copy set",
  shape: "set",
  empty: () => [],
  apply(version, step) {
    switch (step.op) {
      case "add":
      case "setItem":
        return version.includes(step.value) ? version : [...version, step.value];
      case "addRange":
        return [...new Set([...version, ...step.items])];
      case "remove":
        return version.includes(step.value)
          ? version.filter((value) => value !== step.value)
          : version;
      case "clear":
        return [];
    }
  },
  count: (version) => version.length,
  contents: (version) => [...version],
  contains: (version, value) => version.includes(value),
  tryGetValue: (version, value) => [version.includes(value), value],
};

/** A plain-copy dictionary over insertion-ordered Maps. */
const copyDictionarySubject: ModelSubject<ReadonlyMap<number, number>> = {
  name: "copy dictionary",
  shape: "dictionary",
  empty: () => new Map(),
  apply(version, step) {
    switch (step.op) {
      case "add": {
        if (!version.has(step.value)) {
          return new Map(version).set(step.value, step.item);
        }

        if (version.get(step.value) === step.item) {
          return version;
        }

        throw new DuplicateKey();
      }

      case "addRange":
        return new Map([...version, ...step.items.map((key) => [key, 0] as const)]);
      case "setItem":
        return version.get(step.value) === step.item
          ? version
          : new Map(version).set(step.value, step.item);
      case "remove": {
        if (!version.has(step.value)) {
          return version;
        }

        const next = new Map(version);

        next.delete(step.value);

        return next;
      }

      case "clear":
        return new Map();
    }
  },
  count: (version) => version.size,
  contents: (version) => [...version].map(([key, value]) => [key, value]),
  contains: (version, value) => version.has(value),
  tryGetValue: (version, value, missing) =>
    version.has(value) ? [true, version.get(value)] : [false, missing],
};

const options = { steps: 3000, maxVersions: 24 };

describe("the persistent-collection model", () => {
  for (const seed of modelSeeds()) {
    test(`accepts correct subjects (seed ${seed})`, () => {
      runPersistentModel(tipListSubject(true), createSeededRandom(seed), options);
      runPersistentModel(copySetSubject, createSeededRandom(seed), options);
      runPersistentModel(copyDictionarySubject, createSeededRandom(seed), options);
    });
  }

  test("rejects a subject whose versions alias the shared storage", () => {
    expect(() =>
      runPersistentModel(tipListSubject(false), createSeededRandom(1), options),
    ).toThrow();
  });
});
