import {
  describe,
  expect,
  test,
} from "bun:test";
import {
  asArray,
  asJsArray,
  asReadOnlySpan,
  asSpan,
  fromEnd,
  isIterable,
} from "#/system/collections/helpers.ts";
import { List } from "#/system/collections/list.ts";
import { IndexOutOfRangeException } from "#/system/exceptions/index-out-of-range-exception.ts";

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
