import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { decimalRegistry } from "./registries.ts";

describe("decimal arithmetic against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("decimal.json", decimalRegistry)).toEqual([]);
  });
});
