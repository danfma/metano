import { describe, expect, test } from "bun:test";
import { ArgumentException, ArgumentNullException } from "#/system/exceptions/index.ts";
import { StringComparison, stringReplace } from "#/system/string-members.ts";

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
  test.each(
    dotnetReplacements,
  )("%p.Replace(%p, %p) is %p", (value, oldValue, newValue, expected) => {
    expect(stringReplace(value, oldValue, newValue)).toBe(expected);
  });

  test("throws an ArgumentException with .NET's message on an empty old value", () => {
    expect(() => stringReplace("ab", "", "z")).toThrow(ArgumentException);

    expect(() => stringReplace("ab", "", "z")).toThrow(
      "The value cannot be an empty string. (Parameter 'oldValue')",
    );
  });

  test("throws an ArgumentNullException with .NET's message on a null old value", () => {
    expect(() => stringReplace("ab", null as unknown as string, "z")).toThrow(
      ArgumentNullException,
    );

    expect(() => stringReplace("ab", null as unknown as string, "z")).toThrow(
      "Value cannot be null. (Parameter 'oldValue')",
    );
  });

  test("a case-insensitive comparison matches the old value in any case", () => {
    expect(stringReplace("aXbxX", "x", "-", StringComparison.OrdinalIgnoreCase)).toBe("a-b--");
    expect(stringReplace("aXbxX", "x", "-", StringComparison.Ordinal)).toBe("aXb-X");
    expect(stringReplace("aXbxX", "XX", "$&", StringComparison.OrdinalIgnoreCase)).toBe("aXb$&");
  });
});
