import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { stringRegistry } from "./registries.ts";

describe("string members against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("strings.json", stringRegistry)).toEqual([]);
  });
});
