import { describe, expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";

import {
  ImmutableListBuilder,
  toImmutableList,
} from "#/system/collections/immutable-list-builder.ts";

import { List } from "#/system/collections/list.ts";

// Messages read from .NET 10 (2026-11-19).
const OUT_OF_RANGE = "Specified argument was out of the range of valid values.";

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
});
