import { describe, expect, test } from "bun:test";
import { JsonNumberText, treeWithDoubles } from "#/system/json/exact-json-number";

describe("treeWithDoubles", () => {
  test("reads every leaf of the subtree as the number JSON.parse gives", () => {
    const tree = {
      a: [1, new JsonNumberText("12345678901234567890")],
      b: { c: new JsonNumberText("0.30000000000000004") },
    };

    expect(treeWithDoubles(tree)).toEqual({
      a: [1, 12345678901234567000],
      b: { c: 0.30000000000000004 },
    });
  });

  test("copies only the containers that hold a leaf", () => {
    const untouched = { x: [1, 2] };
    const tree = { untouched, touched: [new JsonNumberText("12345678901234567890")] };
    const converted = treeWithDoubles(tree) as typeof tree;

    expect(converted).not.toBe(tree);
    expect(converted.untouched).toBe(untouched);
    expect(tree.touched[0]).toBeInstanceOf(JsonNumberText);
  });

  test("returns a tree without leaves as is", () => {
    const tree = { a: [1, "x", null, { b: true }] };

    expect(treeWithDoubles(tree)).toBe(tree);
  });

  test("keeps __proto__ an own property", () => {
    const tree = JSON.parse('{"__proto__":1,"n":0}') as Record<string, unknown>;

    tree.n = new JsonNumberText("12345678901234567890");

    const converted = treeWithDoubles(tree) as Record<string, unknown>;

    expect(Object.getPrototypeOf(converted)).toBe(Object.prototype);
    expect(Object.keys(converted)).toEqual(["__proto__", "n"]);
  });
});
