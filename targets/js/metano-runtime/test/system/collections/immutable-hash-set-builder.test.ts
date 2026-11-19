import { describe, expect, test } from "bun:test";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";

import {
  ImmutableHashSetBuilder,
  toImmutableHashSet,
} from "#/system/collections/immutable-hash-set-builder.ts";

const ignoreCase = {
  equals: (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value: string): number => value.toLowerCase().length,
};

describe("ImmutableHashSetBuilder (W4-11 condition 4: never alias)", () => {
  test("ToImmutable, then a further builder mutation changes no immutable value", () => {
    const builder = new ImmutableHashSetBuilder<number>();

    builder.add(1);

    const set = toImmutableHashSet(builder);

    builder.add(2);
    builder.remove(1);

    expect([...set]).toEqual([1]);
  });

  test("ToBuilder keeps the immutable source's comparer", () => {
    const set = ImmutableHashSet.of(["Abc"], ignoreCase);

    expect(new ImmutableHashSetBuilder(set).keyComparer).toBe(ignoreCase);
  });
});
