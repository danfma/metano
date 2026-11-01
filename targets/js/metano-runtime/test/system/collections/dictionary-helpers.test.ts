import { describe, expect, test } from "bun:test";
import { addEntry, getValueOrDefault, tryGetValue } from "#/system/collections/dictionary-helpers.ts";

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

describe("tryGetValue", () => {
  test("reports the stored value when the key is present", () => {
    expect(tryGetValue(new Map([["a", 3]]), "a", 0)).toEqual([true, 3]);
  });

  test("reports the missing value when the key is absent", () => {
    expect(tryGetValue(new Map<string, number>(), "a", 0)).toEqual([false, 0]);
    expect(tryGetValue(new Map<string, string>(), "a", null)).toEqual([false, null]);
  });

  test("reports a stored null as found", () => {
    const map = new Map<string, string | null>([["a", null]]);

    expect(tryGetValue(map, "a", "fallback")).toEqual([true, null]);
  });

  test("narrows the value on the found flag", () => {
    const [found, value] = tryGetValue(new Map([["a", "x"]]), "a", null);

    expect(found ? value.length : -1).toBe(1);
  });
});

describe("addEntry", () => {
  test("stores a new entry", () => {
    const map = new Map<string, number>();

    addEntry(map, "a", 1);

    expect([...map]).toEqual([["a", 1]]);
  });

  test("throws on a duplicate key without overwriting the stored value", () => {
    const map = new Map([[1, "a"]]);

    expect(() => addEntry(map, 1, "b")).toThrow(
      "An item with the same key has already been added. Key: 1",
    );
    expect(map.get(1)).toBe("a");
  });

  test("treats a stored null value as present", () => {
    const map = new Map<string, string | null>([["a", null]]);

    expect(() => addEntry(map, "a", "b")).toThrow("Key: a");
  });

  test("throws on a null key", () => {
    const map = new Map<string | null, number>();

    expect(() => addEntry(map, null, 1)).toThrow("Value cannot be null. (Parameter 'key')");
    expect(map.size).toBe(0);
  });
});
