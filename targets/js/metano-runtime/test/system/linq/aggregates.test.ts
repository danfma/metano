import { describe, expect, test } from "bun:test";
import Decimal from "decimal.js";
import { InvalidOperationException } from "#/system/exceptions/index.ts";
import {
  average,
  averageDecimal,
  averageNullable,
  averageNullableDecimal,
  except,
  intersect,
  linq,
  max,
  maxBy,
  maxByNullable,
  maxNullable,
  min,
  minBy,
  minByNullable,
  minNullable,
  select,
  sum,
  sumDecimal,
  toArray,
  union,
} from "#/system/linq/index.ts";

const NO_ELEMENTS = new InvalidOperationException("Sequence contains no elements");

const d = (value: string | number) => new Decimal(value);

/** Every expected value below was printed by the same LINQ call on .NET 10.0. */
describe("Min and Max", () => {
  test("an empty non-nullable sequence throws", () => {
    expect(() => linq([] as number[], min())).toThrow(NO_ELEMENTS);
    expect(() => linq([] as number[], max())).toThrow(NO_ELEMENTS);
    expect(() => linq([] as Decimal[], max())).toThrow(NO_ELEMENTS);
  });

  test("an empty or all-null nullable sequence gives null, and nulls are skipped", () => {
    expect(linq([] as (number | null)[], minNullable())).toBeNull();
    expect(linq([null, null] as (number | null)[], minNullable())).toBeNull();
    expect(linq([null, 4] as (number | null)[], minNullable())).toBe(4);
    expect(linq([null, 4, 9] as (number | null)[], maxNullable())).toBe(9);
    expect(linq([] as string[], minNullable())).toBeNull();
  });

  test("double.NaN wins Min and loses Max, wherever it stands", () => {
    expect(linq([1, Number.NaN, 2], min())).toBeNaN();
    expect(linq([Number.NaN, 1], min())).toBeNaN();
    expect(linq([1, Number.NaN, 2], max())).toBe(2);
    expect(linq([Number.NaN, 1], max())).toBe(1);
    expect(linq([Number.NaN], max())).toBeNaN();
    expect(linq([null, Number.NaN, 1], minNullable())).toBeNaN();
    expect(linq([null, Number.NaN, 1], maxNullable())).toBe(1);
  });

  test("-0 and 0 keep the first one met", () => {
    expect(Object.is(linq([0, -0], min()), 0)).toBe(true);
    expect(Object.is(linq([-0, 0], min()), -0)).toBe(true);
    expect(Object.is(linq([-0, 0], max()), -0)).toBe(true);
  });

  test("strings are ordered, by code unit", () => {
    expect(linq(["pear", "apple"], min())).toBe("apple");
    expect(linq(["pear", "apple"], max())).toBe("pear");
  });

  test("decimals are ordered by value, not by text", () => {
    expect(linq([d(1.5), d(2.5), d(4)], max())).toEqual(d(4));
    expect(linq([d(1.5), d(2.5), d(4)], min((x) => x.neg()))).toEqual(d(-4));
    expect(linq([null, d(10), d(9)], minNullable())).toEqual(d(9));
  });

  test("a selector reads each element", () => {
    const people = [{ name: "b" }, { name: "a" }];
    expect(linq(people, min((p) => p.name))).toBe("a");
    expect(linq([] as { name: string }[], minNullable((p) => p.name))).toBeNull();
  });
});

describe("MinBy and MaxBy", () => {
  const people = [
    { name: "b", age: 1 },
    { name: "a", age: 2 },
  ];

  test("pick the first element on the smallest or largest key", () => {
    expect(linq(people, minBy((p) => p.name))).toBe(people[1]);
    expect(linq(people, maxBy((p) => p.name))).toBe(people[0]);
    expect(linq(people, minByNullable((p) => p.age))).toBe(people[0]);
  });

  test("an empty sequence throws for a value element and gives null for a nullable one", () => {
    expect(() => linq([] as number[], minBy((x) => x))).toThrow(NO_ELEMENTS);
    expect(() => linq([] as number[], maxBy((x) => x))).toThrow(NO_ELEMENTS);
    expect(linq([] as typeof people, minByNullable((p) => p.age))).toBeNull();
    expect(linq([] as typeof people, maxByNullable((p) => p.age))).toBeNull();
  });

  test("null keys are skipped, and the first element stands in when all are null", () => {
    const rows = [
      { id: 1, key: null as number | null },
      { id: 2, key: 5 as number | null },
    ];
    expect(linq(rows, minByNullable((r) => r.key))).toBe(rows[1]);
    expect(linq(rows, minByNullable(() => null))).toBe(rows[0]);
  });

  test("decimal keys are ordered by value", () => {
    const rows = [{ key: d(10) }, { key: d(9) }];
    expect(linq(rows, minBy((r) => r.key))).toBe(rows[1]);
  });
});

describe("Sum and Average", () => {
  test("number sums treat null as zero", () => {
    expect(linq([null, 3, 4] as (number | null)[], sum())).toBe(7);
    expect(linq([] as number[], sum())).toBe(0);
  });

  test("a nullable average skips nulls and gives null when none is left", () => {
    expect(linq([null, 3, 4] as (number | null)[], averageNullable())).toBe(3.5);
    expect(linq([] as (number | null)[], averageNullable())).toBeNull();
    expect(() => linq([] as number[], average())).toThrow(NO_ELEMENTS);
  });

  test("decimals sum and average as decimals, not as text", () => {
    const m = [d("1.5"), d("2.5"), d(4)];
    expect(linq(m, sumDecimal(Decimal)).toString()).toBe("8");
    expect(linq(m.slice(0, 2), averageDecimal()).toString()).toBe("2");
    expect(linq([] as Decimal[], sumDecimal(Decimal)).toString()).toBe("0");
    expect(linq([null, d("1.5")], sumDecimal(Decimal)).toString()).toBe("1.5");
    expect(linq([d(1), d(2), d(2)], averageDecimal()).toString()).toBe("1.6666666666666666667");
    expect(() => linq([] as Decimal[], averageDecimal())).toThrow(NO_ELEMENTS);
    expect(linq([] as Decimal[], averageNullableDecimal())).toBeNull();
    expect(linq([null, d("1.5"), d("2.5")], averageNullableDecimal()).toString()).toBe("2");
  });

  test("a decimal selector reads each element", () => {
    const rows = [{ price: d("1.5") }, { price: d("2.5") }];
    expect(linq(rows, sumDecimal(Decimal, (r) => r.price)).toString()).toBe("4");
  });
});

describe("Union, Intersect and Except", () => {
  test("Union keeps the distinct elements of both, source first", () => {
    expect(linq([1, 2, 2, 3], union([3, 4, 4, 1, 5]), toArray())).toEqual([1, 2, 3, 4, 5]);
  });

  test("Intersect keeps the distinct source elements the other holds, in source order", () => {
    expect(linq([5, 1, 1, 2, 3], intersect([3, 1, 1, 9]), toArray())).toEqual([1, 3]);
  });

  test("Except keeps the distinct source elements the other lacks, in source order", () => {
    expect(linq([5, 1, 1, 2, 3, 5], except([3, 9]), toArray())).toEqual([5, 1, 2]);
  });

  test("elements compare by value: records, strings, NaN", () => {
    expect(linq(["a", "b"], union(["b", "c"]), toArray())).toEqual(["a", "b", "c"]);
    expect(linq([Number.NaN, 1], intersect([Number.NaN]), toArray())).toEqual([Number.NaN]);
  });

  test("they are lazy and re-enumerable", () => {
    const seen = linq([1, 2, 3], except([2]), select((x) => x * 2));
    expect([...seen]).toEqual([2, 6]);
    expect([...seen]).toEqual([2, 6]);
  });

  test("empty operands", () => {
    expect(linq([] as number[], union([1]), toArray())).toEqual([1]);
    expect(linq([1], intersect([] as number[]), toArray())).toEqual([]);
    expect(linq([1], except([] as number[]), toArray())).toEqual([1]);
  });
});
