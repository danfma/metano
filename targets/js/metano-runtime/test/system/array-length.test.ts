import { describe, expect, test } from "bun:test";
import { arrayLength } from "#/system/array-length.ts";
import { OverflowException } from "#/system/exceptions/index.ts";

describe("arrayLength", () => {
  test("passes a non-negative length and throws OverflowException for a negative one", () => {
    expect(arrayLength(3)).toBe(3);
    expect(() => arrayLength(-1)).toThrow(OverflowException);
    expect(() => arrayLength(-1)).toThrow("Arithmetic operation resulted in an overflow.");
  });
});
