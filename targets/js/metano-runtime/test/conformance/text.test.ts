import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { textRegistry } from "./registries.ts";

describe("default text of bool, double and float against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("text.json", textRegistry)).toEqual([]);
  });
});
