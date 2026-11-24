import { describe, expect, test } from "bun:test";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";

import {
  ImmutableArrayBuilder,
  toImmutableArray,
} from "#/system/collections/immutable-array-builder.ts";

import { ImmutableListBuilder } from "#/system/collections/immutable-list-builder.ts";
import { List } from "#/system/collections/list.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";
import { specializeCollection } from "#/system/reflection/specialize-collection.ts";

// Messages read from .NET 10 (2026-11-19).
const OUTSIDE = "Index was outside the bounds of the array.";
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";

function caught(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("ImmutableArrayBuilder", () => {
  test("indexOf and lastIndexOf honour startIndex and count", () => {
    const builder = new ImmutableArrayBuilder([10, 20, 30, 20, 40, 20]);

    expect([
      builder.indexOf(20),
      builder.indexOf(20, 2),
      builder.indexOf(20, 0, 1),
      builder.indexOf(20, 2, 2),
    ]).toEqual([1, 3, -1, 3]);

    expect([
      builder.lastIndexOf(20),
      builder.lastIndexOf(20, 4),
      builder.lastIndexOf(20, 2, 1),
      builder.lastIndexOf(20, 4, 2),
    ]).toEqual([5, 3, -1, 3]);

    expect(() => builder.indexOf(20, 7)).toThrow("(Parameter 'startIndex')");
    expect(() => builder.indexOf(20, 6)).toThrow("(Parameter 'startIndex')");
    expect(new ImmutableArrayBuilder<number>().indexOf(20, 0)).toBe(-1);
    expect(() => builder.indexOf(20, 2, 9)).toThrow("(Parameter 'count')");
    expect(() => builder.lastIndexOf(20, 9)).toThrow("(Parameter 'startIndex')");
    expect(() => builder.lastIndexOf(20, 1, 5)).toThrow("(Parameter 'count')");
    expect(new ImmutableArrayBuilder<number>().lastIndexOf(1)).toBe(-1);
  });

  test("is a sibling of ImmutableListBuilder and composes a List", () => {
    const builder = new ImmutableArrayBuilder<number>();

    expect(builder).not.toBeInstanceOf(ImmutableListBuilder);
    expect(new ImmutableListBuilder<number>()).not.toBeInstanceOf(ImmutableArrayBuilder);
    expect(builder).not.toBeInstanceOf(List);
  });

  test("ToImmutable builds an ImmutableArray that a later mutation never reaches", () => {
    const builder = new ImmutableArrayBuilder<number>([1]);
    const array = toImmutableArray(builder);

    builder.add(2);
    builder.set(0, 9);

    expect(array).toBeInstanceOf(ImmutableArray);
    expect([...array]).toEqual([1]);
    expect(array._items).not.toBe(builder._list._items);
  });

  test("an ImmutableArray source lends its type arguments; the source is unchanged", () => {
    const source = ImmutableArray.of([1, 2], [Int32Type]);
    const builder = new ImmutableArrayBuilder(source);

    builder.add(3);

    expect([...source]).toEqual([1, 2]);
    expect([...toImmutableArray(builder)]).toEqual([1, 2, 3]);

    expect(String(builder)).toBe(
      "System.Collections.Immutable.ImmutableArray`1+Builder[System.Int32]",
    );

    expect(String(toImmutableArray(builder))).toContain("ImmutableArray`1[System.Int32]");
  });

  test("no version check: a loop that writes or adds never throws", () => {
    const builder = new ImmutableArrayBuilder([1, 2]);
    const seen: number[] = [];

    for (const item of builder) {
      seen.push(item);

      if (builder.count < 4) {
        builder.add(item * 10);
      }

      builder.set(0, 7);
    }

    expect(seen).toEqual([1, 2, 10, 20]);
    expect(builder.count).toBe(4);
  });

  test("the indexer throws IndexOutOfRangeException; removeAt / insert keep ArgumentOutOfRangeException", () => {
    const builder = new ImmutableArrayBuilder([1]);

    for (const action of [
      () => builder.get(1),
      () => builder.get(-1),
      () => builder.set(1, 3),
      () => builder.fromEnd(2),
    ]) {
      const error = caught(action);

      expect(error.name).toBe("IndexOutOfRangeException");
      expect(error.message).toBe(OUTSIDE);
    }

    expect(caught(() => builder.removeAt(1)).message).toBe(`${OUT_OF_RANGE} (Parameter 'index')`);
    expect(caught(() => builder.insert(2, 1)).message).toBe(`${OUT_OF_RANGE} (Parameter 'index')`);
  });

  test("class literal and closeFor", () => {
    const builder = new ImmutableArrayBuilder<number>(null, [Int32Type]);

    expect(Object.keys(builder)).toEqual([]);
    expect(ImmutableArrayBuilder.$runtimeType.test(builder)).toBe(true);

    expect(getRuntimeType(builder)).toBe(
      specializeCollection(ImmutableArrayBuilder.$runtimeType, [Int32Type]),
    );

    expect(getRuntimeType(new ImmutableArrayBuilder())).toBe(ImmutableArrayBuilder.$runtimeType);
  });

  test("length, toArray and zero-copy live asJsArray", () => {
    const builder = new ImmutableArrayBuilder([1, 2]);
    const view = builder.asJsArray();
    const snapshot = builder.toArray();

    expect(builder.length).toBe(2);
    expect(view).toBe(builder._list.asJsArray());

    builder.add(3);
    snapshot[0] = 99;

    expect(builder.length).toBe(3);
    expect(view).toEqual([1, 2, 3]);
    expect(builder.get(0)).toBe(1);
    expect([...toImmutableArray(builder)]).toEqual([1, 2, 3]);

    builder.add(4);
    expect(view).toEqual([1, 2, 3, 4]);
  });
});
