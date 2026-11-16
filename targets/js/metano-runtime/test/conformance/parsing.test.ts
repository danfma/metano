import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { parsingRegistry } from "./registries.ts";

describe("parsing helpers against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("parsing.json", parsingRegistry)).toEqual([]);
  });
});
