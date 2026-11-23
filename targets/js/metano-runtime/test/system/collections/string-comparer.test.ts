import {
  describe,
  expect,
  test,
} from "bun:test";

import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { List } from "#/system/collections/list.ts";
import { StringComparer } from "#/system/collections/string-comparer.ts";

describe("StringComparer", () => {
  test("ordinal compares code units as they are", () => {
    expect(StringComparer.ordinal.equals("a", "A")).toBe(false);
    expect(StringComparer.ordinal.equals(null, null)).toBe(true);
    expect(StringComparer.ordinal.equals("a", null)).toBe(false);
    expect(StringComparer.ordinal.compare("B", "a")).toBeLessThan(0);
    expect(StringComparer.ordinal.compare(null, "")).toBeLessThan(0);
    expect(StringComparer.ordinal.compare("x", "x")).toBe(0);
  });

  test("ordinalIgnoreCase folds by the simple uppercase mapping", () => {
    const comparer = StringComparer.ordinalIgnoreCase;

    expect(comparer.equals("abc", "ABC")).toBe(true);
    expect(comparer.equals("ß", "SS")).toBe(false);
    expect(comparer.getHashCode("abc")).toBe(comparer.getHashCode("ABC"));
    expect(comparer.compare("a", "B")).toBeLessThan(0);
    expect(comparer.compare("b", "A")).toBeGreaterThan(0);
  });

  test("it is the comparer a dictionary, a set and a sort take", () => {
    const map = new Dictionary<string, number>(null, StringComparer.ordinalIgnoreCase);
    const set = new HashSet<string>(null, StringComparer.ordinalIgnoreCase);
    const names = List.of(["b", "A", "c"]);

    map.set("A", 1);
    map.set("a", 2);
    set.add("x");
    set.add("X");
    names.sort(StringComparer.ordinalIgnoreCase);

    expect(map.count).toBe(1);
    expect(map.get("A")).toBe(2);
    expect(set.count).toBe(1);
    expect([...names]).toEqual(["A", "b", "c"]);
  });
});
