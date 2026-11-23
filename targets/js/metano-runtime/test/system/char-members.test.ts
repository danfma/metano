import { describe, expect, test } from "bun:test";
import {
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
  ])("%p", (test_, accepted, rejected) => {
    for (const c of accepted) expect(test_(c)).toBe(true);
    for (const c of rejected) expect(test_(c)).toBe(false);
  });
});
