import { describe, expect, test } from "bun:test";

import {
  immutableInsert,
  immutableRemove,
  immutableRemoveAt,
  } from "#/system/collections/list-helpers.ts";

describe("immutableInsert", () => {
  test("inserts at the start", () => {
    const arr = [2, 3];
    const out = immutableInsert(arr, 0, 1);
    expect(out).toEqual([1, 2, 3]);
    expect(arr).toEqual([2, 3]); // unchanged
  });

  test("inserts at the end", () => {
    const arr = [1, 2];
    const out = immutableInsert(arr, 2, 3);
    expect(out).toEqual([1, 2, 3]);
    expect(arr).toEqual([1, 2]);
  });

  test("inserts in the middle", () => {
    const arr = [1, 3];
    const out = immutableInsert(arr, 1, 2);
    expect(out).toEqual([1, 2, 3]);
  });

  test("returns a new array reference", () => {
    const arr = [1];
    const out = immutableInsert(arr, 0, 0);
    expect(out).not.toBe(arr);
  });
});

describe("immutableRemoveAt", () => {
  test("removes the first element", () => {
    const arr = [1, 2, 3];
    const out = immutableRemoveAt(arr, 0);
    expect(out).toEqual([2, 3]);
    expect(arr).toEqual([1, 2, 3]);
  });

  test("removes the last element", () => {
    const arr = [1, 2, 3];
    const out = immutableRemoveAt(arr, 2);
    expect(out).toEqual([1, 2]);
  });

  test("removes a middle element", () => {
    const arr = [1, 2, 3];
    const out = immutableRemoveAt(arr, 1);
    expect(out).toEqual([1, 3]);
  });
});

describe("immutableRemove", () => {
  test("returns a new array without the first occurrence", () => {
    const arr = [1, 2, 3];
    const out = immutableRemove(arr, 2);
    expect(out).toEqual([1, 3]);
    expect(arr).toEqual([1, 2, 3]);
  });

  test("returns the original (same reference) when item not found", () => {
    const arr = [1, 2, 3];
    const out = immutableRemove(arr, 99);
    expect(out).toBe(arr);
  });

  test("removes only the first occurrence on duplicates", () => {
    const arr = [1, 2, 2, 3];
    const out = immutableRemove(arr, 2);
    expect(out).toEqual([1, 2, 3]);
  });
});
