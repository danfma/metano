import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import { Currency } from "#/sample-operator-overloading/currency";
import { PriceRule } from "#/sample-operator-overloading/price-rule";
import { Tagged } from "#/sample-operator-overloading/tagged";

// The generated dispatchers take what call sites emit: omitted defaulted
// arguments and `params` arrays passed expanded (an explicit C# array is
// spread). A single array argument for `params` is deliberately not accepted.
describe("Overload dispatch with defaults and params", () => {
  test("constructor applies the C# default when the argument is omitted", () => {
    expect(new PriceRule().description).toBe("10% off");
    expect(new PriceRule(25).description).toBe("25% off");
  });

  test("constructor params overload takes zero, one and many trailing arguments", () => {
    expect(new PriceRule("VIP").description).toBe("VIP with 0 tiers");
    expect(new PriceRule("VIP", 5).description).toBe("VIP with 1 tiers");
    expect(new PriceRule("VIP", 5, 10, 15).description).toBe("VIP with 3 tiers");
    expect(new PriceRule("VIP", ...[5, 10]).description).toBe("VIP with 2 tiers");
  });

  test("method fast path applies an enum default", () => {
    const rule = new PriceRule();

    expect(rule.label()).toBe("10% off in dollars");
    expect(rule.label(Currency.Eur)).toBe("10% off abroad");
    expect(rule.label(true)).toBe("10% OFF");
  });

  test("static fast path applies a decimal default and accepts a Decimal", () => {
    expect(PriceRule.threshold(3)).toBe("3 units over 1.5");
    expect(PriceRule.threshold(3, new Decimal("2.25"))).toBe("3 units over 2.25");
    expect(PriceRule.threshold("A1")).toBe("code A1");
  });

  test("static params overload yields to the exact-arity overload", () => {
    expect(PriceRule.sum()).toBe(0);
    expect(PriceRule.sum(4)).toBe(4);
    expect(PriceRule.sum(1, 2, 3)).toBe(6);
    expect(PriceRule.sum("label", 7)).toBe(7);
  });

  test("a call matching no overload throws", () => {
    expect(() => new PriceRule(true as unknown as number)).toThrow("No matching constructor");
    expect(() => PriceRule.sum(1, "x" as unknown as number)).toThrow(
      "No matching overload for sum",
    );
  });
});

describe("Overload dispatch with a type-parameter-typed parameter", () => {
  test("a primitive type argument reaches the generic overload", () => {
    expect(new Tagged<string>("v").kind).toBe("value");
    expect(new Tagged<boolean>(true).kind).toBe("value");
    expect(new Tagged<number>(1.5).kind).toBe("value");
    expect(new Tagged<string>("v", "x").kind).toBe("value:x");
  });

  test("the concrete overload of the same arity is tried first", () => {
    expect(new Tagged<string>(3).kind).toBe("code");
  });
});
