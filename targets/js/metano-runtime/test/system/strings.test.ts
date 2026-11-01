import { describe, expect, test } from "bun:test";
import { isNullOrEmpty, isNullOrWhiteSpace, stringReplace } from "#/system/strings.ts";

/** Every BMP code point .NET 10's `char.IsWhiteSpace` accepts. */
const dotnetWhiteSpace = [
  0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0020, 0x0085, 0x00a0, 0x1680, 0x2000, 0x2001,
  0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029,
  0x202f, 0x205f, 0x3000,
];

describe("isNullOrEmpty", () => {
  test("is true for null, undefined and the empty string", () => {
    expect(isNullOrEmpty(null)).toBe(true);
    expect(isNullOrEmpty(undefined)).toBe(true);
    expect(isNullOrEmpty("")).toBe(true);
  });

  test("is false for any other string, white space included", () => {
    expect(isNullOrEmpty(" ")).toBe(false);
    expect(isNullOrEmpty("a")).toBe(false);
  });
});

describe("isNullOrWhiteSpace", () => {
  test("is true for null, undefined and the empty string", () => {
    expect(isNullOrWhiteSpace(null)).toBe(true);
    expect(isNullOrWhiteSpace(undefined)).toBe(true);
    expect(isNullOrWhiteSpace("")).toBe(true);
  });

  test("accepts exactly the BMP characters .NET treats as white space", () => {
    const accepted: number[] = [];

    for (let code = 0; code <= 0xffff; code++) {
      if (isNullOrWhiteSpace(String.fromCharCode(code))) {
        accepted.push(code);
      }
    }

    expect(accepted).toEqual(dotnetWhiteSpace);
  });

  test("diverges from trim() where .NET does: U+0085 is white space, U+FEFF is not", () => {
    expect(isNullOrWhiteSpace("\u0085")).toBe(true);
    expect(isNullOrWhiteSpace("\uFEFF")).toBe(false);
  });

  test("is false as soon as one character is not white space", () => {
    expect(isNullOrWhiteSpace(" \t\n")).toBe(true);
    expect(isNullOrWhiteSpace(" \tx\n")).toBe(false);
  });
});

/** Expected values were produced by C# `string.Replace` on .NET 10. */
const dotnetReplacements: [string, string, string | null, string][] = [
  ["banana", "a", "b", "bbnbnb"],
  ["a.a.b", "a.", "$&", "$&$&b"],
  ["a$b", "$", "$$", "a$$b"],
  ["aaa", "aa", "b", "ba"],
  ["abab", "ab", "", ""],
  ["xaxbx", "x", null, "ab"],
  ["", "x", "y", ""],
  ["a\uD83D\uDE00b\uD83D\uDE00", "\uD83D\uDE00", "-", "a-b-"],
];

describe("stringReplace", () => {
  test.each(dotnetReplacements)(
    "%p.Replace(%p, %p) is %p",
    (value, oldValue, newValue, expected) => {
      expect(stringReplace(value, oldValue, newValue)).toBe(expected);
    },
  );

  test("throws the ArgumentException message on an empty old value", () => {
    expect(() => stringReplace("ab", "", "z")).toThrow(
      "The value cannot be an empty string. (Parameter 'oldValue')",
    );
  });

  test("throws the ArgumentNullException message on a null old value", () => {
    expect(() => stringReplace("ab", null as unknown as string, "z")).toThrow(
      "Value cannot be null. (Parameter 'oldValue')",
    );
  });
});
