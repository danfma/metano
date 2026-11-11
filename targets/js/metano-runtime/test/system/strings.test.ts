import { describe, expect, test } from "bun:test";
import { ArgumentException, ArgumentNullException } from "#/system/exceptions/index.ts";
import {
  alignText,
  alignValue,
  isNullOrEmpty,
  isNullOrWhiteSpace,
  stringReplace,
} from "#/system/strings.ts";

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

  test("throws an ArgumentException with .NET's message on an empty old value", () => {
    expect(() => stringReplace("ab", "", "z")).toThrow(ArgumentException);
    expect(() => stringReplace("ab", "", "z")).toThrow(
      "The value cannot be an empty string. (Parameter 'oldValue')",
    );
  });

  test("throws an ArgumentNullException with .NET's message on a null old value", () => {
    expect(() => stringReplace("ab", null as unknown as string, "z")).toThrow(ArgumentNullException);
    expect(() => stringReplace("ab", null as unknown as string, "z")).toThrow(
      "Value cannot be null. (Parameter 'oldValue')",
    );
  });
});

/** C#'s `{value,alignment}` hole on .NET 10: `[{"ab",5}][{"ab",-5}][{null,4}][{12345,3}]`. */
describe("alignValue", () => {
  test("pads the hole's text with spaces, on the right when the alignment is negative", () => {
    expect(alignValue("ab", 5)).toBe("   ab");
    expect(alignValue("ab", -5)).toBe("ab   ");
    expect(alignValue(true, 6)).toBe("  true");
  });

  test("a null value pads the empty string", () => {
    expect(alignValue(null, 4)).toBe("    ");
    expect(alignValue(undefined, -2)).toBe("  ");
  });

  test("text longer than the alignment is not truncated", () => {
    expect(alignValue(12345, 3)).toBe("12345");
  });
});

describe("alignText", () => {
  test("pads to the alignment, on the left when positive and on the right when negative", () => {
    expect(alignText("ab", 5)).toBe("   ab");
    expect(alignText("ab", -5)).toBe("ab   ");
  });

  test("an undefined alignment leaves the text as it is", () => {
    expect(alignText(" ab ")).toBe(" ab ");
    expect(alignText("", undefined)).toBe("");
  });

  test("a zero or shorter alignment does not truncate", () => {
    expect(alignText("abc", 0)).toBe("abc");
    expect(alignText("abc", -2)).toBe("abc");
  });
});
