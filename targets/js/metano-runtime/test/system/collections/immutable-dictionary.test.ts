import { describe, expect, test } from "bun:test";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

// Messages and same-instance results read from .NET 10 (2026-11-19).

const ignoreCase = {
  equals: (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase(),
  getHashCode: (value: string): number => value.toLowerCase().length,
};

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
