import { describe, expect, test } from "bun:test";

import { SwitchExpressionException, throwError } from "#/system/exceptions/index.ts";

describe("throwError", () => {
  test("throws the given error unchanged", () => {
    const error = new SwitchExpressionException(5);

    expect(() => throwError(error)).toThrow(error);
  });

  test("keeps the type of the other branch in a conditional chain", () => {
    const pick = (value: number): string => (value === 1 ? "one" : throwError(new Error("none")));

    expect(pick(1)).toBe("one");
    expect(() => pick(2)).toThrow("none");
  });
});
