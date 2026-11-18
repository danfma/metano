import { describe, expect, test } from "bun:test";
import { Decimal as DecimalJs } from "decimal.js";
import { Decimal } from "#/system/decimal";

describe("Decimal", () => {
  test("is decimal.js configured for System.Decimal", () => {
    expect(Decimal.precision).toBe(29);
    expect(Decimal.rounding).toBe(DecimalJs.ROUND_HALF_EVEN);
  });

  test("rounds a midpoint to the even neighbor", () => {
    expect(new Decimal("2.5").toDecimalPlaces(0).toFixed()).toBe("2");
    expect(new Decimal("3.5").toDecimalPlaces(0).toFixed()).toBe("4");
  });
});
