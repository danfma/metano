import { describe, expect, test } from "bun:test";
import {
  iCollectionType,
  iDictionaryType,
  iReadOnlyDictionaryType,
  iReadOnlyListType,
  iReadOnlySetType,
  iSetType,
  sortedDictionaryType,
  sortedListType,
  sortedSetType,
} from "#/system/collections/descriptors.ts";
import { SortedDictionary } from "#/system/collections/sorted-dictionary.ts";
import { SortedList } from "#/system/collections/sorted-list.ts";
import { SortedSet } from "#/system/collections/sorted-set.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";

describe("sorted-class descriptors", () => {
  test("factories are memoized", () => {
    expect(sortedSetType(Int32Type)).toBe(sortedSetType(Int32Type));
    expect(sortedDictionaryType(StringType, Int32Type)).toBe(sortedDictionaryType(StringType, Int32Type));
    expect(sortedListType(StringType, Int32Type)).toBe(sortedListType(StringType, Int32Type));
    expect(sortedSetType(Int32Type).name).toBe("SortedSet<Int32>");
  });

  test("GetType() closes over the slot: GetType() == typeof(SortedSet<int>)", () => {
    expect(getRuntimeType(SortedSet.of([1], null, [Int32Type]))).toBe(sortedSetType(Int32Type));
    expect(getRuntimeType(new SortedDictionary(null, null, [StringType, Int32Type]))).toBe(
      sortedDictionaryType(StringType, Int32Type),
    );
    expect(getRuntimeType(new SortedList(null, null, [StringType, Int32Type]))).toBe(
      sortedListType(StringType, Int32Type),
    );
    expect(getRuntimeType(new SortedSet())).toBe(SortedSet.$runtimeType);
  });

  test("the closed test compares type arguments; an absent slot is a wildcard", () => {
    const ints = sortedSetType(Int32Type);

    expect(ints.test(SortedSet.of([1], null, [Int32Type]))).toBe(true);
    expect(ints.test(SortedSet.of(["a"], null, [StringType]))).toBe(false);
    expect(ints.test(new SortedSet())).toBe(true);
  });

  test("interface tests are nominal through each class literal", () => {
    const set = new SortedSet<number>();
    const dictionary = new SortedDictionary<string, number>();
    const list = new SortedList<string, number>();

    expect(iSetType(Int32Type).test(set)).toBe(true);
    expect(iReadOnlySetType(Int32Type).test(set)).toBe(true);
    expect(iCollectionType(Int32Type).test(set)).toBe(true);
    expect(iReadOnlyListType(Int32Type).test(set)).toBe(false);
    expect(iDictionaryType(StringType, Int32Type).test(dictionary)).toBe(true);
    expect(iReadOnlyDictionaryType(StringType, Int32Type).test(list)).toBe(true);
    expect(iSetType(Int32Type).test(list)).toBe(false);
  });

  test("toString prints .NET's type text", () => {
    expect(String(SortedSet.of([1], null, [Int32Type]))).toBe("System.Collections.Generic.SortedSet`1[System.Int32]");
    expect(String(new SortedSet())).toBe("System.Collections.Generic.SortedSet`1[T]");
    expect(String(new SortedDictionary(null, null, [Int32Type, Int32Type]))).toBe(
      "System.Collections.Generic.SortedDictionary`2[System.Int32,System.Int32]",
    );
    expect(String(new SortedList())).toBe("System.Collections.Generic.SortedList`2[TKey,TValue]");
  });
});
