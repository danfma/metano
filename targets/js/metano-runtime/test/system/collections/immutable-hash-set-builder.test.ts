import { describe, expect, test } from "bun:test";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";

import {
  ImmutableHashSetBuilder,
  toImmutableHashSet,
} from "#/system/collections/immutable-hash-set-builder.ts";

import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";
import { specializeCollection } from "#/system/reflection/specialize-collection.ts";

/** Whether `action` invalidates an enumeration started before it (the builder's version moved). */
function invalidates<B extends Iterable<unknown>>(
  builder: B,
  action: (builder: B) => unknown,
): boolean {
  const iterator = builder[Symbol.iterator]();

  action(builder);

  try {
    iterator.next();

    return false;
  } catch {
    return true;
  }
}

const ignoreCase = {
  equals: (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value: string): number => value.toLowerCase().length,
};

describe("ImmutableHashSetBuilder.tryGetValue", () => {
  test("a miss returns the searched value, as .NET's", () => {
    const builder = new ImmutableHashSetBuilder<string>(["a"]);

    expect(builder.tryGetValue("a", null)).toEqual([true, "a"]);
    expect(builder.tryGetValue("missing", null)).toEqual([false, "missing"]);
  });
});

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

  test("every mutating call bumps the version as .NET 10, a no-op included", () => {
    const builder = (): ImmutableHashSetBuilder<number> => new ImmutableHashSetBuilder([1, 2]);

    expect(invalidates(builder(), (b) => b.add(1))).toBe(true);
    expect(invalidates(builder(), (b) => b.remove(9))).toBe(true);
    expect(invalidates(builder(), (b) => b.unionWith([]))).toBe(true);
    expect(invalidates(builder(), (b) => b.exceptWith([9]))).toBe(true);
    expect(invalidates(builder(), (b) => b.intersectWith([1, 2]))).toBe(true);
    expect(invalidates(builder(), (b) => b.symmetricExceptWith([]))).toBe(true);
    expect(invalidates(new ImmutableHashSetBuilder<number>(), (b) => b.clear())).toBe(true);
    expect(invalidates(builder(), (b) => b.contains(1))).toBe(false);
  });

  test("class literal and closeFor; an ImmutableHashSet source lends its type arguments", () => {
    const builder = new ImmutableHashSetBuilder(ImmutableHashSet.of([1], null, [Int32Type]));

    expect(Object.keys(builder)).toEqual([]);
    expect(ImmutableHashSetBuilder.$runtimeType.test(builder)).toBe(true);

    expect(getRuntimeType(builder)).toBe(
      specializeCollection(ImmutableHashSetBuilder.$runtimeType, [Int32Type]),
    );

    expect(String(builder)).toBe(
      "System.Collections.Immutable.ImmutableHashSet`1+Builder[System.Int32]",
    );
  });
});
