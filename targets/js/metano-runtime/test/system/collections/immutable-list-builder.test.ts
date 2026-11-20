import { describe, expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";

import {
  ImmutableListBuilder,
  toImmutableList,
} from "#/system/collections/immutable-list-builder.ts";

import { List } from "#/system/collections/list.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type } from "#/system/reflection/primitive-types.ts";
import { specializeCollection } from "#/system/reflection/specialize-collection.ts";

// Messages read from .NET 10 (2026-11-19).
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";

/** Whether `action` invalidates an enumeration started before it (the builder's version moved). */
function invalidates<B extends Iterable<unknown>>(
  builder: B,
  action: (builder: B) => unknown,
): boolean {
  const iterator = builder[Symbol.iterator]();

  action(builder);

  try {
    iterator.next();

    return false;
  } catch {
    return true;
  }
}

describe("ImmutableListBuilder (W4-11 condition 4: never alias)", () => {
  test("ToImmutable, then a further builder mutation changes no immutable value", () => {
    const builder = new ImmutableListBuilder<number>();

    builder.add(1);

    const list = toImmutableList(builder);

    builder.add(2);
    builder.set(0, 9);

    expect([...list]).toEqual([1]);
    expect(list.add(3)._items).not.toBe(builder._list._items);
  });

  test("ToBuilder copies, and the immutable source is unchanged by the builder", () => {
    const list = ImmutableList.of([1, 2]);
    const builder = new ImmutableListBuilder(list);

    builder.add(3);

    expect([...list]).toEqual([1, 2]);
    expect([...toImmutableList(builder)]).toEqual([1, 2, 3]);
  });

  test("a builder's index checks use .NET's builder text", () => {
    const builder = new ImmutableListBuilder<number>();

    expect(() => builder.get(0)).toThrow(`${OUT_OF_RANGE} (Parameter 'index')`);
    expect(builder).not.toBeInstanceOf(List);
    expect(String(builder)).toBe("System.Collections.Immutable.ImmutableList`1+Builder[T]");
  });

  test("every mutating call bumps the version as .NET 10, a no-op included", () => {
    const builder = (): ImmutableListBuilder<number> => new ImmutableListBuilder([2, 1]);

    expect(invalidates(builder(), (b) => b.removeAll((v) => v > 9))).toBe(true);
    expect(invalidates(builder(), (b) => b.set(0, 2))).toBe(true);
    expect(invalidates(builder(), (b) => b.addRange([]))).toBe(true);
    expect(invalidates(builder(), (b) => b.sort())).toBe(true);
    expect(invalidates(builder(), (b) => b.reverse())).toBe(true);
    expect(invalidates(new ImmutableListBuilder<number>(), (b) => b.clear())).toBe(true);
    expect(invalidates(builder(), (b) => b.insertRange(0, []))).toBe(true);
    expect(invalidates(builder(), (b) => b.remove(1))).toBe(true);
  });

  test("indexOf and lastIndexOf honour startIndex and count", () => {
    const builder = new ImmutableListBuilder([10, 20, 30, 20, 40, 20]);

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

    // .NET's ImmutableList reports the count, then the index.
    expect(() => builder.indexOf(20, 7)).toThrow("(Parameter 'count')");
    expect(() => builder.indexOf(20, -1)).toThrow("(Parameter 'index')");
    expect(() => builder.indexOf(20, 2, 9)).toThrow("(Parameter 'count')");
    expect(() => builder.lastIndexOf(20, 9)).toThrow("(Parameter 'count')");
    expect(() => builder.lastIndexOf(20, 1, 9)).toThrow("(Parameter 'count')");
    expect(() => builder.lastIndexOf(20, 0, 2)).toThrow(
      "Value does not fall within the expected range.",
    );
    expect(new ImmutableListBuilder<number>().lastIndexOf(1)).toBe(-1);
  });

  test("lastIndexOf edge ranges follow .NET's check order", () => {
    const builder = new ImmutableListBuilder([10, 20, 30]);
    const empty = new ImmutableListBuilder<number>();

    expect(builder.lastIndexOf(20, 5, 1)).toBe(-1);
    expect(() => builder.lastIndexOf(20, 3)).toThrow("(Parameter 'count')");
    expect(() => empty.lastIndexOf(20, -1, 0)).toThrow("(Parameter 'index')");
    expect(() => empty.lastIndexOf(20, 0, -1)).toThrow("(Parameter 'count')");
    expect(() => empty.lastIndexOf(20, 0, 5)).toThrow("(Parameter 'count')");
    expect(empty.lastIndexOf(20)).toBe(-1);
    expect(empty.lastIndexOf(20, 0)).toBe(-1);
  });

  test("remove of an absent element is the one call that does not bump", () => {
    expect(invalidates(new ImmutableListBuilder([1]), (b) => b.remove(9))).toBe(false);
    expect(invalidates(new ImmutableListBuilder([1]), (b) => b.get(0))).toBe(false);
  });

  test("class literal and closeFor; an ImmutableList source lends its type arguments", () => {
    const builder = new ImmutableListBuilder(ImmutableList.of([1], [Int32Type]));

    expect(Object.keys(builder)).toEqual([]);
    expect(ImmutableListBuilder.$runtimeType.test(builder)).toBe(true);

    expect(getRuntimeType(builder)).toBe(
      specializeCollection(ImmutableListBuilder.$runtimeType, [Int32Type]),
    );

    expect(String(builder)).toBe(
      "System.Collections.Immutable.ImmutableList`1+Builder[System.Int32]",
    );
  });
});
