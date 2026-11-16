import { describe, expect, test } from "bun:test";
import { Temporal } from "@js-temporal/polyfill";
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
    [
      "throwIfNegative",
      () => throwIfNegative(-3, "neg"),
      "neg ('-3') must be a non-negative value.",
    ],
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
  });

  test("a decimal negative zero is negative and zero, as on .NET", () => {
    expect(() => throwIfNegative(new Decimal("-0.0"), "d")).toThrow(ArgumentOutOfRangeException);
    expect(() => throwIfZero(new Decimal("-0.0"), "d")).toThrow(ArgumentOutOfRangeException);
    expect(() => throwIfNegative(new Decimal("0.0"), "d")).not.toThrow();
  });

  test("a null parameter name leaves the name empty, as on .NET", () => {
    expect(() => throwIfZero(0, null)).toThrow(" ('0') must be a non-zero value.");
  });

  // Outcomes printed by .NET 10: the sign guards test the sign bit, the ordered ones
  // follow double.CompareTo (NaN is the least value) and the equality ones double.Equals.
  test.each<[string, () => void, boolean]>([
    ["ThrowIfNegative(NaN)", () => throwIfNegative(Number.NaN, "n"), true],
    ["ThrowIfNegative(-0)", () => throwIfNegative(-0, "n"), true],
    ["ThrowIfNegativeOrZero(NaN)", () => throwIfNegativeOrZero(Number.NaN, "n"), true],
    ["ThrowIfNegativeOrZero(-0)", () => throwIfNegativeOrZero(-0, "n"), true],
    ["ThrowIfZero(-0)", () => throwIfZero(-0, "n"), true],
    ["ThrowIfZero(NaN)", () => throwIfZero(Number.NaN, "n"), false],
    ["ThrowIfEqual(NaN, NaN)", () => throwIfEqual(Number.NaN, Number.NaN, "n"), true],
    ["ThrowIfNotEqual(NaN, NaN)", () => throwIfNotEqual(Number.NaN, Number.NaN, "n"), false],
    ["ThrowIfEqual(0, -0)", () => throwIfEqual(0, -0, "n"), true],
    ["ThrowIfGreaterThan(NaN, 0)", () => throwIfGreaterThan(Number.NaN, 0, "n"), false],
    ["ThrowIfLessThan(NaN, 0)", () => throwIfLessThan(Number.NaN, 0, "n"), true],
    ["ThrowIfLessThan(0, NaN)", () => throwIfLessThan(0, Number.NaN, "n"), false],
    [
      "ThrowIfGreaterThanOrEqual(NaN, NaN)",
      () => throwIfGreaterThanOrEqual(Number.NaN, Number.NaN, "n"),
      true,
    ],
  ])("%s throws as on .NET", (_, call, throws) => {
    if (throws) {
      expect(call).toThrow(ArgumentOutOfRangeException);
    } else {
      expect(call).not.toThrow();
    }
  });

  test("-0 keeps its sign in the message, as on .NET", () => {
    expect(() => throwIfNegative(-0, "z")).toThrow(
      "z ('-0') must be a non-negative value. (Parameter 'z')\nActual value was -0.",
    );

  });

  test("other kinds compare as .NET does", () => {
    expect(() => throwIfGreaterThan("b", "a", "c")).toThrow(
      "c ('b') must be less than or equal to 'a'.",
    );

    expect(() => throwIfZero("\0", "c")).toThrow(ArgumentOutOfRangeException);
    expect(() => throwIfGreaterThan(true, false, "b")).toThrow(ArgumentOutOfRangeException);
    expect(() => throwIfGreaterThan(false, true, "b")).not.toThrow();

    const later = Temporal.PlainDate.from("2020-02-01");
    const earlier = Temporal.PlainDate.from("2020-01-01");

    expect(() => throwIfGreaterThan(later, earlier, "d")).toThrow(ArgumentOutOfRangeException);
    expect(() => throwIfLessThan(later, earlier, "d")).not.toThrow();

    const record = { x: 1, equals: (other: { x: number }) => other.x === 1 };

    expect(() => throwIfEqual(record, { x: 1, equals: () => true }, "r")).toThrow(
      ArgumentOutOfRangeException,
    );

    expect(() => throwIfNotEqual("a", "a", "s")).not.toThrow();
  });

  // EqualityComparer<T>.Default outcomes printed by .NET 10 for the kinds the compiler
  // maps the equality guards for: tuples (arrays) and [PlainObject] records by value,
  // a TimeSpan (a Temporal.Duration) by its length, null equal to null.
  test.each<[string, unknown, unknown, boolean]>([
    ["tuples", [1, 2], [1, 2], true],
    ["nested tuples", [[1, 2], "a"], [[1, 2], "a"], true],
    ["different tuples", [1, 2], [1, 3], false],
    ["[PlainObject] records", { name: "a", age: 1 }, { name: "a", age: 1 }, true],
    ["durations", Temporal.Duration.from({ minutes: 5 }), Temporal.Duration.from({ seconds: 300 }), true],
    ["different durations", Temporal.Duration.from({ minutes: 5 }), Temporal.Duration.from({ minutes: 6 }), false],
    ["dates", Temporal.PlainDate.from("2024-01-02"), Temporal.PlainDate.from("2024-01-02"), true],
    ["decimals", new Decimal("1.0"), new Decimal("1"), true],
    ["nulls", null, null, true],
  ])("throwIfEqual compares %s as .NET does", (_, value, other, equal) => {
    if (equal) {
      expect(() => throwIfEqual(value, other, "v")).toThrow(ArgumentOutOfRangeException);
      expect(() => throwIfNotEqual(value, other, "v")).not.toThrow();
    } else {
      expect(() => throwIfEqual(value, other, "v")).not.toThrow();
      expect(() => throwIfNotEqual(value, other, "v")).toThrow(ArgumentOutOfRangeException);
    }
  });
});
