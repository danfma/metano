import { describe, expect, test } from "bun:test";
import { ArgumentException } from "#/system/exceptions/index.ts";
import { stringCompare, stringEquals } from "#/system/string-comparison.ts";
import { StringComparison, stringContains, stringIndexOf } from "#/system/string-members.ts";

const { Ordinal, OrdinalIgnoreCase, InvariantCulture, InvariantCultureIgnoreCase } =
  StringComparison;

/** `string.Equals(left, right, OrdinalIgnoreCase)` on .NET 10. */
const dotnetOrdinalIgnoreCaseEquality: [string, string, boolean][] = [
  ["ß", "SS", false],
  ["ß", "ẞ", false],
  ["İ", "i", false],
  ["İ", "I", false],
  ["ı", "I", false],
  ["ı", "i", false],
  ["ﬀ", "FF", false],
  ["ς", "σ", true],
  ["ς", "Σ", true],
  ["σ", "Σ", true],
  ["ſ", "S", false],
  ["K", "k", true],
  ["K", "k", false],
  ["µ", "Μ", true],
  ["ǅ", "Ǆ", true],
  ["ǅ", "ǆ", true],
  ["ᾀ", "ᾈ", true],
  ["𐐨", "𐐀", true],
  ["a\uD801", "A\uD801", true],
];

/** `Math.Sign(string.Compare(left, right, comparison))` on .NET 10. */
const dotnetCompareSigns: [string, string, StringComparison, number][] = [
  ["a", "B", OrdinalIgnoreCase, -1],
  ["_", "a", OrdinalIgnoreCase, 1],
  ["_", "A", Ordinal, 1],
  ["abc", "ABD", Ordinal, 1],
  ["ab", "a", Ordinal, 1],
  ["a", "ab", OrdinalIgnoreCase, -1],
  ["￿", "𐐨", Ordinal, 1],
  ["￿", "𐐨", OrdinalIgnoreCase, -1],
  ["𐐨", "é", OrdinalIgnoreCase, 1],
  ["éa", "Éb", OrdinalIgnoreCase, -1],
  ["a", "A", InvariantCulture, -1],
  ["a", "B", InvariantCulture, -1],
  ["résumé", "resume", InvariantCulture, 1],
  ["coop", "co-op", InvariantCulture, 1],
  ["ß", "ẞ", InvariantCulture, -1],
  ["σ", "Σ", InvariantCulture, -1],
];

describe("stringEquals", () => {
  test.each(
    dotnetOrdinalIgnoreCaseEquality,
  )("%p equals %p ignoring case: %p", (left, right, expected) => {
    expect(stringEquals(left, right, OrdinalIgnoreCase)).toBe(expected);
  });

  test("null equals only null", () => {
    expect(stringEquals(null, null, OrdinalIgnoreCase)).toBe(true);
    expect(stringEquals("a", null, Ordinal)).toBe(false);
    expect(stringEquals(null, "a", InvariantCulture)).toBe(false);
  });

  test("the culture members compare with a collator", () => {
    expect(stringEquals("ab", "a­b", InvariantCulture)).toBe(true);
    expect(stringEquals("ﬀ", "FF", InvariantCultureIgnoreCase)).toBe(true);
    expect(stringEquals("ß", "SS", InvariantCultureIgnoreCase)).toBe(false);
    expect(stringEquals("a", "A", InvariantCulture)).toBe(false);
  });

  test("an unknown comparison throws, even for equal strings", () => {
    expect(() => stringEquals("a", "a", 9 as StringComparison)).toThrow(ArgumentException);
  });
});

describe("stringCompare", () => {
  test.each(dotnetCompareSigns)("%p vs %p (%p) has sign %p", (left, right, comparison, sign) => {
    expect(Math.sign(stringCompare(left, right, comparison))).toBe(sign);
  });

  test("the ordinal forms return .NET's magnitude for a differing code unit", () => {
    expect(stringCompare("a", "z", Ordinal)).toBe(-25);
    expect(stringCompare("az", "a{", OrdinalIgnoreCase)).toBe(-33);
    expect(stringCompare("é", "A", OrdinalIgnoreCase)).toBe(136);
  });

  test("null sorts first", () => {
    expect(stringCompare(null, null, Ordinal)).toBe(0);
    expect(stringCompare(null, "a", OrdinalIgnoreCase)).toBe(-1);
    expect(stringCompare("a", null, InvariantCulture)).toBe(1);
  });

  test("an unknown comparison throws with .NET's message", () => {
    expect(() => stringCompare("a", "b", 9 as StringComparison)).toThrow(
      "The string comparison type passed in is currently not supported. (Parameter 'comparisonType')",
    );
  });
});

describe("case-insensitive searches share the ordinal fold", () => {
  test("dotless i and long s do not match ASCII", () => {
    expect(stringContains("ı", "I", OrdinalIgnoreCase)).toBe(false);
    expect(stringContains("ſ", "s", OrdinalIgnoreCase)).toBe(false);
  });

  test("a surrogate pair folds as one code point", () => {
    expect(stringIndexOf("x𐐨", "𐐀", 0, undefined, OrdinalIgnoreCase)).toBe(1);
  });
});
