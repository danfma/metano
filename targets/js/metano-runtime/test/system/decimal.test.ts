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

  test("keeps constructor and copied scale in default text and concatenation", () => {
    const value = new Decimal("2.50");

    expect(value.toString()).toBe("2.50");
    expect(`v=${value}`).toBe("v=2.50");
    expect("v=" + value).toBe("v=2.50");
    expect(new Decimal(value).toString()).toBe("2.50");
    expect(new Decimal("0.00").toString()).toBe("0.00");
    expect(new Decimal("2.500e1").toString()).toBe("25.00");
  });
});
