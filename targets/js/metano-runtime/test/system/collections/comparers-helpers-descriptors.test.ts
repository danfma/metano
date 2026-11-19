import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import {
  createComparer,
  defaultCompare,
  defaultComparer,
  defaultEqualityComparer,
  elementEquals,
  elementHashCode,
} from "#/system/collections/default-comparer.ts";
import {
  dictionaryType,
  hashSetType,
  iListType,
  iReadOnlyCollectionType,
  iReadOnlyListType,
  listType,
  queueType,
  stackType,
} from "#/system/collections/descriptors.ts";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import {
  asArray,
  asJsArray,
  asReadOnlySpan,
  asSpan,
  fromEnd,
  isIterable,
} from "#/system/collections/helpers.ts";
import { List } from "#/system/collections/list.ts";
import { Queue } from "#/system/collections/queue.ts";
import { ArgumentException } from "#/system/exceptions/argument-exception.ts";
import { IndexOutOfRangeException } from "#/system/exceptions/index-out-of-range-exception.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";
import { ObjectType, UnknownType } from "#/system/reflection/well-known-types.ts";

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

describe("helpers", () => {
  test("fromEnd on a native array is checked", () => {
    expect(fromEnd([1, 2, 3], 1)).toBe(3);
    expect(() => fromEnd([1], 2)).toThrow(IndexOutOfRangeException);
    expect(() => fromEnd([1], 0)).toThrow("Index was outside the bounds of the array.");
  });

  test("isIterable", () => {
    expect(isIterable("ab")).toBe(true);
    expect(isIterable(List.of([1]))).toBe(true);
    expect(isIterable({})).toBe(false);
    expect(isIterable(null)).toBe(false);
  });

  test("asArray unwraps arrays and fixed-size adopted lists only", () => {
    const array = [1];

    expect(asArray(array)).toBe(array);
    expect(asArray(List.adopt(array, { fixedSize: true }))).toBe(array);
    expect(asArray(List.of([1]))).toBeNull();
    expect(asArray("x")).toBeNull();
  });

  test("the views return the backing array; null handling", () => {
    const list = List.of([1]);

    expect(asJsArray(list)).toBe(list.asJsArray());
    expect(asSpan(list)).toBe(list.asJsArray());
    expect(asReadOnlySpan(list)).toBe(list.asJsArray());
    expect(asSpan(null)).toEqual([]);
    expect(asReadOnlySpan(undefined)).toEqual([]);
    expect(() => asJsArray(null)).toThrow("Value cannot be null. (Parameter 'list')");
  });
});

describe("descriptor factories and GetType()", () => {
  test("memoized on argument identity", () => {
    expect(listType(StringType)).toBe(listType(StringType));
    expect(listType(StringType)).not.toBe(listType(Int32Type));
    expect(listType(StringType).name).toBe("List<String>");
    expect(listType(StringType).fullName).toBe("System.Collections.Generic.List<System.String>");
    expect(dictionaryType(StringType, Int32Type).typeArguments).toEqual([StringType, Int32Type]);
  });

  test("class tests: instanceof plus the slot by identity, absent slot a wildcard", () => {
    expect(listType(StringType).test(List.of(["a"], [StringType]))).toBe(true);
    expect(listType(StringType).test(List.of([1], [Int32Type]))).toBe(false);
    expect(listType(StringType).test(List.of([1]))).toBe(true);
    expect(listType(StringType).test(["a"])).toBe(false);
    expect(hashSetType(Int32Type).test(HashSet.of([1], null, [Int32Type]))).toBe(true);
    expect(queueType(Int32Type).test(new Queue<number>())).toBe(true);
    expect(stackType(Int32Type).test(new Queue<number>())).toBe(false);
  });

  test("interface tests are nominal; read-only interfaces are covariant", () => {
    const strings = List.of(["a"], [StringType]);

    expect(iListType(StringType).test(strings)).toBe(true);
    expect(iListType(ObjectType).test(strings)).toBe(false);
    expect(iReadOnlyListType(ObjectType).test(strings)).toBe(true);
    expect(iListType(StringType).test(List.adopt(["a"], { fixedSize: true }))).toBe(true);
    expect(iListType(StringType).test(HashSet.of(["a"]))).toBe(false);
    expect(iReadOnlyCollectionType(UnknownType).test(Dictionary.of([["a", 1]]))).toBe(true);
    expect(iReadOnlyCollectionType(Int32Type).test(new Queue<number>())).toBe(true);
  });

  test("getRuntimeType closes over the slot: GetType() == typeof(List<int>)", () => {
    expect(getRuntimeType(List.of([1], [Int32Type]))).toBe(listType(Int32Type));
    expect(getRuntimeType(List.of([1]))).toBe(List.$runtimeType);
    expect(getRuntimeType(List.adopt([1], { fixedSize: true }, [Int32Type]))).toBe(
      listType(Int32Type),
    );
  });
});
