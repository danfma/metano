import { describe, expect, test } from "bun:test";
import { CurrencyConversionException } from "#/sample-operator-overloading/currency-conversion-exception";
import { InvalidCurrencyCodeException } from "#/sample-operator-overloading/invalid-currency-code-exception";
import { UnsupportedCurrencyException } from "#/sample-operator-overloading/unsupported-currency-exception";

describe("Currency exceptions", () => {
  test("ArgumentException base appends the parameter name like .NET", () => {
    const error = new InvalidCurrencyCodeException("XYZ");

    expect(error).toBeInstanceOf(Error);
    expect(error.message).toBe("Unknown currency code 'XYZ' (Parameter 'code')");
  });

  test("Message override two levels below the BCL base reads the inherited message", () => {
    const error = new UnsupportedCurrencyException("JPY");

    expect(error).toBeInstanceOf(InvalidCurrencyCodeException);
    expect(Object.hasOwn(error, "message")).toBe(false);
    expect(error.message).toBe(
      "Unsupported currency. Unknown currency code 'JPY' (Parameter 'code')",
    );
    expect(String(error)).toBe(
      "Error: Unsupported currency. Unknown currency code 'JPY' (Parameter 'code')",
    );
  });

  test("chained constructor keeps the inner exception as the cause", () => {
    const error = new CurrencyConversionException("USD", "BRL");

    expect(error.message).toBe("Conversion failed: Cannot convert USD to BRL");
    expect(error.cause).toBeInstanceOf(Error);
    expect((error.cause as Error).message).toBe("No exchange rate available");
  });

  test("explicit reason becomes the cause", () => {
    const reason = new Error("rate feed offline");

    const error = new CurrencyConversionException("EUR", "USD", reason);

    expect(error.message).toBe("Conversion failed: Cannot convert EUR to USD");
    expect(error.cause).toBe(reason);
  });
});
