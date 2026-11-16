import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { numberParsingRegistry } from "./registries.ts";

describe("number and boolean parsing against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("number-parsing.json", numberParsingRegistry)).toEqual([]);
  });
});
