import {
  describe,
  expect,
  test,
} from "bun:test";
import { Decimal } from "decimal.js";
import {
  createComparer,
  defaultCompare,
  defaultComparer,
  defaultEqualityComparer,
  elementEquals,
  elementHashCode,
} from "#/system/collections/default-comparer.ts";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";

describe("elementEquals / elementHashCode", () => {
  test("records by value, objects and arrays by reference, NaN equals NaN", () => {
    const record = (x: number) => ({ x, equals: (o: { x: number }) => o.x === x, hashCode: () => x });

    expect(elementEquals(record(1), record(1))).toBe(true);
    expect(elementEquals({}, {})).toBe(false);
    expect(elementEquals([1], [1])).toBe(false);
    expect(elementEquals(Number.NaN, Number.NaN)).toBe(true);
    expect(elementEquals(0, -0)).toBe(true);
  });

  test("equal values hash equally", () => {
    expect(elementHashCode(new Decimal("1.0"))).toBe(elementHashCode(new Decimal(1)));
    expect(elementHashCode(0)).toBe(elementHashCode(-0));
    expect(elementHashCode("a")).toBe(elementHashCode("a"));
    expect(elementHashCode(1.5)).toBe(elementHashCode(1.5));
    expect(elementHashCode(new Date(5))).toBe(elementHashCode(new Date(5)));
    expect(elementHashCode(null)).toBe(0);
  });

  test("an object hashes by identity, stably", () => {
    const value = {};

    expect(elementHashCode(value)).toBe(elementHashCode(value));
  });

  test("the comparer objects", () => {
    expect(defaultEqualityComparer.equals(1, 1)).toBe(true);
    expect(defaultComparer.compare(1, 2)).toBe(-1);
    expect(createComparer((a: number, b: number) => b - a).compare(1, 2)).toBe(1);
  });
});

describe("defaultCompare (Comparer<T>.Default)", () => {
  test("null first, NaN first among numbers, strings ordinally", () => {
    expect(defaultCompare(null, 1)).toBe(-1);
    expect(defaultCompare(1, undefined)).toBe(1);
    expect(defaultCompare(Number.NaN, -1)).toBe(-1);
    expect(defaultCompare(-0, 0)).toBe(0);
    expect(defaultCompare("B", "a")).toBe(-1);
    expect(defaultCompare(false, true)).toBe(-1);
    expect(defaultCompare(2n, 1n)).toBe(1);
  });

  test("compareTo implementers and Decimal", () => {
    const value = { compareTo: (other: number) => 1 - other };

    expect(defaultCompare(value, 3)).toBe(-2);
    expect(defaultCompare(new Decimal(1), new Decimal(2))).toBe(-1);
  });

  test("anything else is not comparable", () => {
    let error: unknown;

    try {
      defaultCompare({}, {});
    } catch (caught) {
      error = caught;
    }

    expect(error).toBeInstanceOf(ArgumentException);
    expect((error as Error).message).toBe("At least one object must implement IComparable.");
  });
});
