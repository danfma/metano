import { describe, expect, test } from "bun:test";
import { comparableCompareTo } from "#/system/comparable-compare-to.ts";
import {
  ArgumentException,
  NotSupportedException,
  NullReferenceException,
} from "#/system/exceptions/index.ts";
import {
  stringCompare,
  stringCompareTo,
  stringEquals,
  stringInstanceEquals,
} from "#/system/string-comparison.ts";
import { stringHashCode } from "#/system/string-hash-code.ts";
import { StringComparison, stringContains, stringIndexOf } from "#/system/string-members.ts";

const {
  CurrentCulture,
  CurrentCultureIgnoreCase,
  Ordinal,
  OrdinalIgnoreCase,
  InvariantCulture,
  InvariantCultureIgnoreCase,
} = StringComparison;

/**
 * `Math.Sign(string.Compare(left, right, comparison))` on .NET 10 with ICU (en-US) for
 * InvariantCultureIgnoreCase, CurrentCultureIgnoreCase and InvariantCulture: the ignore-case
 * members ignore case only, never width or kana type.
 */
const dotnetCultureSigns: [string, string, number, number, number][] = [
  ["\uFF21", "a", 1, 1, 1],
  ["\uFF21", "A", 1, 1, 1],
  ["\uFF41", "A", 1, 1, -1],
  ["\uFF76", "\u30AB", 1, 1, 1],
  ["\u30AB", "\uFF76", -1, -1, -1],
  ["\u3042", "\u30A2", -1, -1, -1],
  ["\u30A2", "\u3042", 1, 1, 1],
  ["\uFB00", "FF", 0, 0, -1],
  ["\uFB00", "ff", 0, 0, 1],
  ["\u00DF", "\u1E9E", 0, 0, -1],
  ["\u00DF", "SS", 1, 1, 1],
  ["\u00E9", "\u00C9", 0, 0, -1],
  ["\u00C9", "e", 1, 1, 1],
  ["Stra\u00DFe", "STRASSE", 1, 1, 1],
  ["\u01C5", "\u01C6", 0, 0, 1],
  ["\u03C2", "\u03A3", 0, 0, 1],
  ["\u2460", "1", 0, 0, 1],
  ["\u00B2", "2", 0, 0, 1],
  ["\u338F", "kg", 0, 0, 1],
  ["\u212B", "\u00E5", 0, 0, 1],
  ["\u212A", "k", 0, 0, 1],
  ["\uFB01", "FI", 0, 0, -1],
  ["\uFF71", "\u30A2", 1, 1, 1],
  ["\uFF76\uFF9E", "\u30AC", 1, 1, 1],
  ["\u0130", "i", 1, 1, 1],
  ["\u0131", "I", 1, 1, 1],
  ["a", "B", -1, -1, -1],
  ["\uFF21b", "ab", 1, 1, 1],
  ["ab", "\uFF21b", -1, -1, -1],
  ["\uFF41\uFF42\uFF43", "ABC", 1, 1, -1],
  ["\uFF76\uFF9E", "\uFF76", 1, 1, 1],
  ["\u30A2\u30A4", "\uFF71\uFF72", -1, -1, -1],
  ["x\u0301", "X\u0301", 0, 0, -1],
  ["\uFF21", "\uFF41", 0, 0, 1],
  ["\u3000", " ", 0, 0, 1],
];

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

describe("culture comparisons", () => {
  test.each(dotnetCultureSigns)(
    "%p vs %p: invariant ignore-case %p, current ignore-case %p, invariant %p",
    (left, right, invariantIgnoreCase, currentIgnoreCase, invariant) => {
      expect(Math.sign(stringCompare(left, right, InvariantCultureIgnoreCase))).toBe(
        invariantIgnoreCase,
      );
      expect(Math.sign(stringCompare(left, right, CurrentCultureIgnoreCase))).toBe(
        currentIgnoreCase,
      );
      expect(Math.sign(stringCompare(left, right, InvariantCulture))).toBe(invariant);
    },
  );

  test("a width form folds its case but keeps its width", () => {
    expect(stringEquals("\uFF21", "\uFF41", InvariantCultureIgnoreCase)).toBe(true);
    expect(stringEquals("\u3000", " ", CurrentCultureIgnoreCase)).toBe(true);
  });

  test("known gaps against .NET on ICU stay as recorded", () => {
    // .NET on ICU: equal for the first three pairs, -1 for the small kana.
    const residues: [string, string][] = [
      ["\u0130", "i\u0307"],
      ["\uFF76\uFF9E", "\uFF76\u3099"],
      ["\uFFE8", "\u2502"],
      ["\uFF71", "\uFF67"],
      ["\u30A2", "\u30A1"],
    ];

    for (const [left, right] of residues) {
      expect(Math.sign(stringCompare(left, right, InvariantCultureIgnoreCase))).toBe(1);
    }
  });

  test("width never compares equal ignoring case", () => {
    expect(stringEquals("\uFF21", "a", InvariantCultureIgnoreCase)).toBe(false);
    expect(stringEquals("\uFF76", "\u30AB", CurrentCultureIgnoreCase)).toBe(false);
  });
});

describe("instance forms", () => {
  test("a null receiver throws NullReferenceException", () => {
    expect(() => stringInstanceEquals(null, "a", Ordinal)).toThrow(NullReferenceException);
    expect(() => stringCompareTo(null, "a")).toThrow(NullReferenceException);
    expect(() => stringHashCode(null, Ordinal)).toThrow(NullReferenceException);
    expect(() => comparableCompareTo(null, 1)).toThrow(NullReferenceException);
  });

  test("Equals(object) is false for a value that is not a string", () => {
    expect(stringInstanceEquals("1", 1, Ordinal)).toBe(false);
    expect(stringInstanceEquals("a", null, Ordinal)).toBe(false);
  });

  test("CompareTo(object) rejects a value that is not a string", () => {
    expect(() => stringCompareTo("a", 5)).toThrow("Object must be of type String.");
    expect(stringCompareTo("a", null)).toBe(1);
    expect(Math.sign(stringCompareTo("a", "B"))).toBe(-1);
  });

  test("GetHashCode(comparison) agrees with the ordinal comparers", () => {
    expect(stringHashCode("Hello", OrdinalIgnoreCase)).toBe(
      stringHashCode("hELLO", OrdinalIgnoreCase),
    );
    expect(() => stringHashCode("a", CurrentCulture)).toThrow(NotSupportedException);
    expect(() => stringHashCode("a", 9 as StringComparison)).toThrow(ArgumentException);
  });

  test("IComparable.CompareTo dispatches by receiver", () => {
    expect(Math.sign(comparableCompareTo("a", "B"))).toBe(-1);
    expect(comparableCompareTo(2, 1)).toBe(1);
    expect(comparableCompareTo({ compareTo: () => -7 }, {})).toBe(-7);
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
