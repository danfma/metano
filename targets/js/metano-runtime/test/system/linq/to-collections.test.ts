import { describe, expect, test } from "bun:test";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { List } from "#/system/collections/list.ts";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { linq, toDictionary, toHashSet, toList } from "#/system/linq/index.ts";

const SOURCE = [3, 1, 2, 1, 3];

const WORDS = ["bb", "a", "ccc"];

function thrown(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

/** Every expected value below was printed by the same LINQ call on .NET 10.0. */
describe("ToList", () => {
  test("copies every element into a new List, in source order", () => {
    const list = linq(SOURCE, toList());

    expect(list).toBeInstanceOf(List);
    expect([...list]).toEqual([3, 1, 2, 1, 3]);
  });

  test("an empty source gives an empty List", () => {
    expect(linq([] as number[], toList()).count).toBe(0);
  });
});

describe("ToHashSet", () => {
  test("collapses duplicates and keeps the first occurrence's order", () => {
    const set = linq(SOURCE, toHashSet());

    expect(set).toBeInstanceOf(HashSet);
    expect([...set]).toEqual([3, 1, 2]);
  });
});

describe("ToDictionary", () => {
  test("with a key selector, the element is the value, in source order", () => {
    const dictionary = linq(
      WORDS,
      toDictionary((word: string) => word.length),
    );

    expect(dictionary).toBeInstanceOf(Dictionary);
    expect([...dictionary]).toEqual([
      [2, "bb"],
      [1, "a"],
      [3, "ccc"],
    ]);
  });

  test("with a value selector, the selected value is stored", () => {
    const dictionary = linq(
      WORDS,
      toDictionary(
        (word: string) => word.length,
        (word: string) => word.toUpperCase(),
      ),
    );

    expect([...dictionary]).toEqual([
      [2, "BB"],
      [1, "A"],
      [3, "CCC"],
    ]);
  });

  test("a repeated key throws ArgumentException with .NET's message", () => {
    const keyOnly = thrown(() => linq(SOURCE, toDictionary((x: number) => x)));

    expect(keyOnly).toBeInstanceOf(ArgumentException);
    expect(keyOnly.message).toBe("An item with the same key has already been added. Key: 1");

    const withValue = thrown(() =>
      linq(
        WORDS,
        toDictionary(
          (word: string) => (word.length > 1 ? "long" : "short"),
          (word: string) => word,
        ),
      ),
    );

    expect(withValue).toBeInstanceOf(ArgumentException);
    expect(withValue.message).toBe(
      "An item with the same key has already been added. Key: long",
    );
  });
});
