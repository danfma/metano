import { describe, expect, test } from "bun:test";

import {
  ImmutableDictionaryBuilder,
  toImmutableDictionary,
} from "#/system/collections/immutable-dictionary-builder.ts";

import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";
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

describe("toImmutableDictionary", () => {
  test("a builder keeps its comparers; any other source is an enumerable of pairs", () => {
    const builder = new ImmutableDictionaryBuilder<string, number>(null, {
      equals: (a: string, b: string) => a.toLowerCase() === b.toLowerCase(),
      getHashCode: (v: string) => v.toLowerCase().length,
    });

    builder.add("k", 5);

    expect(toImmutableDictionary(builder).containsKey("K")).toBe(true);
    expect(toImmutableDictionary([["a", 1] as const]).count).toBe(1);
  });
});

describe("ImmutableDictionaryBuilder (W4-11 condition 4: never alias)", () => {
  test("ToImmutable, then a further builder mutation changes no immutable value", () => {
    const builder = new ImmutableDictionaryBuilder<string, number>();

    builder.set("a", 1);

    const dictionary = toImmutableDictionary(builder);

    builder.set("a", 2);
    builder.set("b", 3);

    expect([...dictionary]).toEqual([["a", 1]]);
  });

  test("every mutating call bumps the version as .NET 10, a no-op included", () => {
    const builder = (): ImmutableDictionaryBuilder<number, number> =>
      new ImmutableDictionaryBuilder([[1, 1]]);

    expect(invalidates(builder(), (b) => b.remove(9))).toBe(true);
    expect(invalidates(builder(), (b) => b.set(1, 1))).toBe(true);
    expect(invalidates(builder(), (b) => b.add(1, 1))).toBe(true);

    expect(invalidates(new ImmutableDictionaryBuilder<number, number>(), (b) => b.clear())).toBe(
      true,
    );

    expect(invalidates(builder(), (b) => b.containsKey(1))).toBe(false);
  });

  test("add of a present key: a no-op for an equal value, ArgumentException for a different one", () => {
    const builder = new ImmutableDictionaryBuilder<number, number>([[1, 1]]);

    builder.add(1, 1);

    expect([...builder]).toEqual([[1, 1]]);

    expect(() => builder.add(1, 2)).toThrow(
      "An element with the same key but a different value already exists. Key: '1'",
    );

    expect(invalidates(builder, (b) => expect(() => b.add(1, 2)).toThrow())).toBe(false);

    const byValueComparer = new ImmutableDictionaryBuilder<number, string>(
      [[1, "a"]],
      null,
      ignoreCase,
    );

    byValueComparer.add(1, "A");

    expect(byValueComparer.get(1)).toBe("a");
    expect(() => byValueComparer.add(null as never, "a")).toThrow("(Parameter 'key')");
  });

  test("class literal and closeFor; an ImmutableDictionary source lends its type arguments", () => {
    const source = ImmutableDictionary.create<string, number>(null, null, [StringType, Int32Type]);
    const builder = new ImmutableDictionaryBuilder(source);

    expect(Object.keys(builder)).toEqual([]);
    expect(ImmutableDictionaryBuilder.$runtimeType.test(builder)).toBe(true);

    expect(getRuntimeType(builder)).toBe(
      specializeCollection(ImmutableDictionaryBuilder.$runtimeType, [StringType, Int32Type]),
    );
  });
});
