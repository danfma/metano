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

  test("retains the clone's shared prototype for construction and arithmetic", () => {
    expect(new Decimal("1") instanceof DecimalJs).toBe(true);
    expect(new DecimalJs("1") instanceof Decimal).toBe(true);
    expect(new Decimal("1").plus(2) instanceof Decimal).toBe(true);
  });

  test("static configuration still governs arithmetic without changing decimal.js", () => {
    const precision = Decimal.precision;
    const externalPrecision = DecimalJs.precision;

    try {
      Decimal.set({ precision: 3 });
      expect(new Decimal(1).div(3).toString()).toBe("0.333");
      expect(DecimalJs.precision).toBe(externalPrecision);
    } finally {
      Decimal.set({ precision });
    }
  });

  test("keeps constructor and copied scale in default text and concatenation", () => {
    const value = new Decimal("2.50");

    expect(value.toString()).toBe("2.50");
    expect(`v=${value}`).toBe("v=2.50");
    expect("v=" + value).toBe("v=2.50");
    expect(new Decimal(value).toString()).toBe("2.50");
    expect(new Decimal("0.00").toString()).toBe("0.00");
    expect(new Decimal("2.500e1").toString()).toBe("25.00");
    expect(Decimal("2.50").toString()).toBe("2.50");
  });
});
