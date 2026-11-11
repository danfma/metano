import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import {
  throwIfEqual,
  throwIfGreaterThan,
  throwIfGreaterThanOrEqual,
  throwIfLessThan,
  throwIfLessThanOrEqual,
  throwIfNegative,
  throwIfNegativeOrZero,
  throwIfNotEqual,
  throwIfNull,
  throwIfNullOrEmpty,
  throwIfNullOrWhiteSpace,
  throwIfZero,
} from "#/system/argument-guards.ts";
import {
  ArgumentException,
  ArgumentNullException,
  ArgumentOutOfRangeException,
} from "#/system/exceptions/index.ts";

/** Messages printed by .NET 10 for the same calls. */
describe("argument guards", () => {
  test("throwIfNull throws ArgumentNullException with the parameter name", () => {
    expect(() => throwIfNull(null, "s")).toThrow(ArgumentNullException);
    expect(() => throwIfNull(undefined, "s")).toThrow("Value cannot be null. (Parameter 's')");
    expect(() => throwIfNull(0, "s")).not.toThrow();
  });

  test("throwIfNullOrEmpty and throwIfNullOrWhiteSpace", () => {
    expect(() => throwIfNullOrEmpty(null, "s")).toThrow(ArgumentNullException);
    expect(() => throwIfNullOrEmpty("", "e")).toThrow(ArgumentException);
    expect(() => throwIfNullOrEmpty("", "e")).toThrow(
      "The value cannot be an empty string. (Parameter 'e')",
    );
    expect(() => throwIfNullOrWhiteSpace("  ", "w")).toThrow(
      "The value cannot be an empty string or composed entirely of whitespace. (Parameter 'w')",
    );
    expect(() => throwIfNullOrWhiteSpace(" a ", "w")).not.toThrow();
  });

  const comparisons: [string, () => void, string][] = [
    ["throwIfZero", () => throwIfZero(0, "zero"), "zero ('0') must be a non-zero value."],
    ["throwIfNegative", () => throwIfNegative(-3, "neg"), "neg ('-3') must be a non-negative value."],
    [
      "throwIfNegativeOrZero",
      () => throwIfNegativeOrZero(0, "zero"),
      "zero ('0') must be a non-negative and non-zero value.",
    ],
    ["throwIfEqual", () => throwIfEqual(-3, -3, "neg"), "neg ('-3') must not be equal to '-3'."],
    ["throwIfNotEqual", () => throwIfNotEqual(-3, 4, "neg"), "neg ('-3') must be equal to '4'."],
    [
      "throwIfGreaterThan",
      () => throwIfGreaterThan(-3, -5, "neg"),
      "neg ('-3') must be less than or equal to '-5'.",
    ],
    [
      "throwIfGreaterThanOrEqual",
      () => throwIfGreaterThanOrEqual(-3, -3, "neg"),
      "neg ('-3') must be less than '-3'.",
    ],
    [
      "throwIfLessThan",
      () => throwIfLessThan(-3, 0, "neg"),
      "neg ('-3') must be greater than or equal to '0'.",
    ],
    [
      "throwIfLessThanOrEqual",
      () => throwIfLessThanOrEqual(-3, -3, "neg"),
      "neg ('-3') must be greater than '-3'.",
    ],
  ];

  test.each(comparisons)("%s throws .NET's ArgumentOutOfRangeException", (_, call, message) => {
    expect(call).toThrow(ArgumentOutOfRangeException);

    try {
      call();
    } catch (error) {
      const exception = error as ArgumentOutOfRangeException;
      const [paramName, value] = /^(\w+) \('([^']*)'\)/.exec(message)!.slice(1);

      expect(exception.message).toBe(
        `${message} (Parameter '${paramName}')\nActual value was ${value}.`,
      );
      expect(exception.paramName).toBe(paramName!);
      expect(String(exception.actualValue)).toBe(value!);
    }
  });

  test("comparisons accept bigint and decimal.js values and pass NaN", () => {
    expect(() => throwIfNegative(-1n, "big")).toThrow("big ('-1') must be a non-negative value.");
    expect(() => throwIfLessThan(new Decimal("1.5"), new Decimal("2.5"), "d")).toThrow(
      "d ('1.5') must be greater than or equal to '2.5'.",
    );
    expect(() => throwIfNegative(Number.NaN, "n")).not.toThrow();
    expect(() => throwIfGreaterThan(Number.NaN, 0, "n")).not.toThrow();
    expect(() => throwIfNegative(-0, "z")).not.toThrow();
  });
});
