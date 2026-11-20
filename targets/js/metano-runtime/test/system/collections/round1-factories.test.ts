import { describe, expect, test } from "bun:test";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { canReuseCollectionTypeArguments } from "#/system/collections/close-collection.ts";
import { StringType, Int32Type } from "#/system/reflection/primitive-types.ts";
import { ObjectType, UnknownType } from "#/system/reflection/well-known-types.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { immutableListType, immutableArrayType, immutableHashSetType } from "#/system/collections/descriptors.ts";

describe("round 1 immutable factory slot reuse", () => {
  test("list reuse requires exactly the destination arguments", () => {
    const source = ImmutableList.of(["x"], [StringType]);
    const widened = ImmutableList.from<unknown>(source, [ObjectType]);
    expect(widened).not.toBe(source);
    expect(getRuntimeType(widened)).toBe(immutableListType(ObjectType));
    expect(immutableListType(StringType).test(widened)).toBe(false);
    expect([...widened]).toEqual(["x"]);
    expect(ImmutableList.from(source, [StringType])).toBe(source);
    expect(ImmutableList.from(source)).toBe(source);
    expect(ImmutableList.from(source, [UnknownType])).toBe(source);
  });

  test("array reuse requires exactly the destination arguments", () => {
    const source = ImmutableArray.of(["x"], [StringType]);
    const widened = ImmutableArray.from<unknown>(source, [ObjectType]);
    expect(widened).not.toBe(source);
    expect(getRuntimeType(widened)).toBe(immutableArrayType(ObjectType));
    expect(immutableArrayType(StringType).test(widened)).toBe(false);
    expect([...widened]).toEqual(["x"]);
    expect(ImmutableArray.from(source, [StringType])).toBe(source);
  });

  test("set reuse requires exact arguments and the same comparer", () => {
    const source = ImmutableHashSet.of(["x"], null, [StringType]);
    const widened = ImmutableHashSet.from<unknown>(source, null, [ObjectType]);
    expect(widened).not.toBe(source);
    expect(getRuntimeType(widened)).toBe(immutableHashSetType(ObjectType));
    expect(immutableHashSetType(StringType).test(widened)).toBe(false);
    expect([...widened]).toEqual(["x"]);
    expect(ImmutableHashSet.from(source, null, [StringType])).toBe(source);
  });

  test("dictionary reuse cannot keep a mismatched value slot", () => {
    const source = ImmutableDictionary.create<number, string>(null, null, [Int32Type, StringType]).add(1, "x");
    const widened = ImmutableDictionary.from<number, unknown>(source, null, null, [Int32Type, ObjectType]);
    expect(widened).not.toBe(source);
    expect(widened.$typeArguments$ImmutableDictionary).toEqual([Int32Type, ObjectType]);
    expect(ImmutableDictionary.from(source, null, null, [Int32Type, StringType])).toBe(source);
  });

  test("open destinations reuse sources; only known differing arguments require copies", () => {
    expect(canReuseCollectionTypeArguments(undefined, null)).toBe(true);
    expect(canReuseCollectionTypeArguments(undefined, [StringType])).toBe(true);
    expect(canReuseCollectionTypeArguments([StringType], undefined)).toBe(true);
    expect(canReuseCollectionTypeArguments([UnknownType], [StringType])).toBe(true);
    expect(canReuseCollectionTypeArguments([StringType], [UnknownType])).toBe(true);
    expect(canReuseCollectionTypeArguments([StringType], [StringType, Int32Type])).toBe(false);
    expect(canReuseCollectionTypeArguments([StringType], [StringType])).toBe(true);
  });
});
