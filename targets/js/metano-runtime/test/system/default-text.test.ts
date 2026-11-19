import { describe, expect, test } from "bun:test";
import { enumText, formatText, formatTuple, objectText } from "#/system/default-text.ts";
import { NullReferenceException } from "#/system/exceptions/null-reference-exception.ts";
import { box } from "#/system/reflection/box.ts";
import type { EnumType } from "#/system/reflection/runtime-type.ts";

const access: EnumType<number> = {
  kind: "enum",
  name: "Access",
  fullName: "Access",
  namespace: "",
  names: ["None", "Read", "Write", "Execute"],
  values: [0, 1, 2, 4],
  isFlags: true,
  test: (value: unknown): value is number => typeof value === "number",
};

describe("default value text", () => {
  test("erased booleans and doubles use the existing invariant formatters", () => {
    expect([true, false, 1e-7, 1e21, -0].map(formatText)).toEqual([
      "True", "False", "1E-07", "1E+21", "-0",
    ]);
  });

  test("implicit null text is empty but explicit object dereference throws", () => {
    expect(formatText(null)).toBe("");
    expect(formatText(undefined)).toBe("");
    expect(() => objectText(null)).toThrow(NullReferenceException);
  });

  test("preserves an ordinary object's ToString override", () => {
    expect(objectText({ toString: () => "custom" })).toBe("custom");
  });

  test("enum names, flags, unknown bits and boxed receivers share one formatter", () => {
    expect([0, 1, 3, 8].map((value) => enumText(value, access))).toEqual([
      "None", "Read", "Read, Write", "8",
    ]);
    expect(enumText(box(3, access), access)).toBe("Read, Write");
    expect(enumText(null, access)).toBe("");
  });

  test("tuple text encloses invariant element text and nested tuples", () => {
    expect(formatTuple([1, "a"])).toBe("(1, a)");
    expect(formatTuple([true, 1e-7, null, [1, "a"]])).toBe("(True, 1E-07, , (1, a))");
    expect(formatTuple([])).toBe("()");
  });
});
