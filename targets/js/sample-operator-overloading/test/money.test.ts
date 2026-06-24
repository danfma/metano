import { describe, expect, test } from "bun:test";
import { Money } from "#/sample-operator-overloading/money";
import { Currency } from "#/sample-operator-overloading/currency";
import { NoSameMoneyCurrencyException } from "#/sample-operator-overloading/no-same-money-currency-exception";

describe("Money operator overloads", () => {
  const usd100 = new Money(10000n, Currency.Usd);
  const usd50 = new Money(5000n, Currency.Usd);
  const eur100 = new Money(10000n, Currency.Eur);

  // ── Addition (+) ──

  test("$add sums two Money values with same currency", () => {
    const result = usd100.$add(usd50);
    expect(result.cents).toBe(15000n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("static __add sums two Money values", () => {
    const result = Money.__add(usd100, usd50);
    expect(result.cents).toBe(15000n);
  });

  test("$add throws on different currencies", () => {
    expect(() => usd100.$add(eur100)).toThrow(NoSameMoneyCurrencyException);
  });

  // ── Subtraction (-) ──

  test("$subtract subtracts two Money values", () => {
    const result = usd100.$subtract(usd50);
    expect(result.cents).toBe(5000n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("$subtract throws on different currencies", () => {
    expect(() => usd100.$subtract(eur100)).toThrow(NoSameMoneyCurrencyException);
  });

  // ── Multiplication (*) — overloaded: Money×bigint and Money×Money ──

  test("$multiply scales by a bigint factor", () => {
    const result = usd100.$multiply(3n);
    expect(result.cents).toBe(30000n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("$multiply multiplies two Money values", () => {
    const usd3 = new Money(300n, Currency.Usd);
    const result = usd100.$multiply(usd3);
    expect(result.cents).toBe(10000n * 300n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("$multiply Money×Money throws on different currencies", () => {
    expect(() => usd100.$multiply(eur100)).toThrow(NoSameMoneyCurrencyException);
  });

  // ── Division (/) — overloaded: Money÷bigint and Money÷Money ──

  test("$divide divides by a bigint divisor", () => {
    const result = usd100.$divide(2n);
    expect(result.cents).toBe(5000n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("$divide divides two Money values", () => {
    const usd2 = new Money(200n, Currency.Usd);
    const result = usd100.$divide(usd2);
    expect(result.cents).toBe(10000n / 200n);
    expect(result.currency).toBe(Currency.Usd);
  });

  test("$divide Money÷Money throws on different currencies", () => {
    expect(() => usd100.$divide(eur100)).toThrow(NoSameMoneyCurrencyException);
  });

  // ── Record helpers ──

  test("with() creates a copy with overrides", () => {
    const changed = usd100.with({ cents: 999n });
    expect(changed.cents).toBe(999n);
    expect(changed.currency).toBe(Currency.Usd);
  });

  test("equals() checks structural equality", () => {
    const copy = new Money(10000n, Currency.Usd);
    expect(usd100.equals(copy)).toBe(true);
    expect(usd100.equals(usd50)).toBe(false);
    expect(usd100.equals(eur100)).toBe(false);
  });
});
