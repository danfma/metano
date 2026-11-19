import { describe, expect, test } from "bun:test";
import { Decimal as DecimalJs } from "decimal.js";
import { Decimal } from "#/system/decimal";
import { formatText, withDecimalScale } from "#/system/default-text.ts";
import { parseDecimal, tryParseDecimal } from "#/system/decimal-parsing.ts";
import { elementEquals, elementHashCode } from "#/system/collections/default-comparer.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { Dictionary } from "#/system/collections/dictionary.ts";

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

  test("keeps emitted literal and parse scale only in default text", () => {
    const value = withDecimalScale(new Decimal("2.50"), 2);

    expect(formatText(value)).toBe("2.50");
    expect("v=" + formatText(value)).toBe("v=2.50");
    expect(formatText(withDecimalScale(new Decimal("0.00"), 2))).toBe("0.00");
    expect(formatText(parseDecimal("2.500", Decimal))).toBe("2.500");
    expect(formatText(tryParseDecimal("2.500", Decimal)[1])).toBe("2.500");
    expect(value.toString()).toBe("2.5");
    expect(value.valueOf()).toBe("2.5");
    expect(String(value)).toBe("2.5");
    expect(Object.hasOwn(value, "toString")).toBe(false);
    expect(Object.hasOwn(value, "valueOf")).toBe(false);
    expect(formatText(new Decimal(Infinity))).toBe("Infinity");
    expect(formatText(new Decimal(NaN))).toBe("NaN");
  });

  test("scale metadata does not change equality, hashing or collection keys", () => {
    const values = [
      withDecimalScale(new Decimal("1.0"), 1),
      withDecimalScale(new Decimal("1.00"), 2),
      new Decimal("0.5").plus(new Decimal("0.5")),
    ];

    for (const value of values) {
      expect(elementEquals(value, values[0])).toBe(true);
      expect(elementHashCode(value)).toBe(elementHashCode(values[0]));
      expect(String(value)).toBe("1");
    }

    expect(new HashSet(values).count).toBe(1);

    const dictionary = new Dictionary<Decimal, string>();
    dictionary.set(values[0]!, "one");
    expect(dictionary.get(values[1]!)).toBe("one");
    dictionary.set(values[2]!, "updated");
    expect(dictionary.count).toBe(1);
    expect(dictionary.get(values[0]!)).toBe("updated");
  });

  test("native construction and scale metadata preserve negative zero conversion", () => {
    const value = new Decimal("-0");

    expect(Object.is(value.toNumber(), -0)).toBe(true);
    expect(value.valueOf()).toBe("-0");
    expect(Object.is(withDecimalScale(value, 2).toNumber(), -0)).toBe(true);
  });
});
