import { describe, expect, test } from "bun:test";
import { getValueOrDefault } from "#/system/collections/dictionary-helpers.ts";

describe("getValueOrDefault", () => {
  test("returns the stored value when the key is present", () => {
    expect(getValueOrDefault(new Map([["a", 3]]), "a", 0)).toBe(3);
  });

  test("returns the default when the key is absent", () => {
    expect(getValueOrDefault(new Map<string, number>(), "a", 0)).toBe(0);
    expect(getValueOrDefault(new Map<string, boolean>(), "a", false)).toBe(false);
  });

  test("returns a stored null instead of the default", () => {
    const map = new Map<string, string | null>([["a", null]]);

    expect(getValueOrDefault(map, "a", "fallback")).toBeNull();
  });

  test("returns a stored falsy value instead of the default", () => {
    expect(getValueOrDefault(new Map([["a", 0]]), "a", 5)).toBe(0);
    expect(getValueOrDefault(new Map([["a", ""]]), "a", "x")).toBe("");
  });
});
