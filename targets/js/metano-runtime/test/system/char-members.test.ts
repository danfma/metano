import { describe, expect, test } from "bun:test";
import {
  charIsAscii,
  charIsAsciiDigit,
  charIsAsciiHexDigit,
  charIsAsciiHexDigitLower,
  charIsAsciiHexDigitUpper,
  charIsAsciiLetter,
  charIsAsciiLetterLower,
  charIsAsciiLetterOrDigit,
  charIsAsciiLetterUpper,
  charIsBetween,
  charIsControl,
  charIsDigit,
  charIsLetter,
  charIsLetterOrDigit,
  charIsLower,
  charIsNumber,
  charIsPunctuation,
  charIsSeparator,
  charIsSymbol,
  charIsUpper,
  charIsWhiteSpace,
} from "#/system/char-members.ts";

describe("char category tests", () => {
  test.each([
    [charIsLetter, ["a", "Z", "é", "ж"], ["1", " ", "_", "\uD83D"]],
    [charIsDigit, ["0", "9", "٣"], ["a", "½", " "]],
    [charIsLetterOrDigit, ["a", "7"], ["_", " "]],
    [charIsNumber, ["5", "½", "Ⅷ"], ["a", "+"]],
    [charIsUpper, ["A", "É"], ["a", "1"]],
    [charIsLower, ["a", "é"], ["A", "1"]],
    [charIsPunctuation, [".", "_", "-"], ["a", "+", "$"]],
    [charIsSymbol, ["+", "$", "<"], ["a", "."]],
    [charIsSeparator, [" ", " ", " "], ["\t", "a"]],
    [charIsControl, ["\0", "\n", "\u0085"], ["a", " "]],
    [charIsWhiteSpace, [" ", "\t", "\n", "\r", "\u000B", "\u0085", " ", " "], ["a", "﻿", "0"]],
    [charIsAscii, ["a", "\u007F"], ["\u0080", "é"]],
    [charIsAsciiDigit, ["0", "9"], ["a", "٣"]],
    [charIsAsciiLetter, ["a", "Z"], ["1", "é"]],
    [charIsAsciiLetterLower, ["a", "z"], ["A", "1"]],
    [charIsAsciiLetterUpper, ["A", "Z"], ["a", "1"]],
    [charIsAsciiLetterOrDigit, ["a", "Z", "5"], ["_", "é"]],
    [charIsAsciiHexDigit, ["0", "f", "F"], ["g", "G"]],
    [charIsAsciiHexDigitLower, ["0", "f"], ["F", "g"]],
    [charIsAsciiHexDigitUpper, ["0", "F"], ["f", "G"]],
  ])("%p", (test_, accepted, rejected) => {
    for (const c of accepted) expect(test_(c)).toBe(true);
    for (const c of rejected) expect(test_(c)).toBe(false);
  });
});

describe("charIsBetween", () => {
  test("is inclusive on both ends", () => {
    expect(charIsBetween("a", "a", "f")).toBe(true);
    expect(charIsBetween("f", "a", "f")).toBe(true);
    expect(charIsBetween("g", "a", "f")).toBe(false);
  });

  test("wraps around like .NET when min is greater than max", () => {
    expect(charIsBetween("z", "f", "a")).toBe(true);
    expect(charIsBetween("0", "f", "a")).toBe(true);
    expect(charIsBetween("c", "f", "a")).toBe(false);
  });
});
