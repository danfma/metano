import { describe, expect, test } from "bun:test";
import { checkRows } from "./conformance-rows.ts";
import { temporalRegistry } from "./registries.ts";

describe("TimeSpan and DateTimeOffset construction against real .NET (tests/BclReference)", () => {
  test("every row agrees", () => {
    expect(checkRows("temporal.json", temporalRegistry)).toEqual([]);
  });
});
