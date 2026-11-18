import { describe, expect, test } from "bun:test";
import { asType, isType } from "#/system/reflection/is-type.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

describe("isType", () => {
  test("delegates to the descriptor's test", () => {
    expect(isType(1, Int32Type)).toBe(true);
    expect(isType("1", Int32Type)).toBe(false);
  });
});

describe("asType", () => {
  test("returns the value when it is of the type, else null", () => {
    expect(asType("a", StringType)).toBe("a");
    expect(asType(1, StringType)).toBeNull();
  });

  test("keeps null as null", () => {
    expect(asType(null, StringType)).toBeNull();
    expect(asType(undefined, StringType)).toBeNull();
  });
});
