import { describe, expect, test } from "bun:test";

import {
  dictionaryType,
  hashSetType,
  iImmutableListType,
  immutableArrayType,
  iCollectionType,
  iDictionaryType,
  iListType,
  iReadOnlyCollectionType,
  iReadOnlyDictionaryType,
  iReadOnlyListType,
  iReadOnlySetType,
  iSetType,
  listType,
  queueType,
  sortedDictionaryType,
  sortedListType,
  sortedSetType,
  stackType,
  immutableListType,
  immutableHashSetType,
  immutableDictionaryType,
  readOnlyListType,
  immutableListBuilderType,
  immutableArrayBuilderType,
  immutableHashSetBuilderType,
  immutableDictionaryBuilderType,
} from "#/system/collections/descriptors.ts";

import { ConcurrentDictionary } from "#/system/collections/concurrent-dictionary.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableArrayBuilder } from "#/system/collections/immutable-array-builder.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { ImmutableDictionaryBuilder } from "#/system/collections/immutable-dictionary-builder.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableHashSetBuilder } from "#/system/collections/immutable-hash-set-builder.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { ImmutableListBuilder } from "#/system/collections/immutable-list-builder.ts";
import { ReadOnlyList } from "#/system/collections/read-only-list.ts";
import { SortedDictionary } from "#/system/collections/sorted-dictionary.ts";
import { SortedList, SortedListView } from "#/system/collections/sorted-list.ts";
import { SortedSet } from "#/system/collections/sorted-set.ts";
import { getRuntimeType } from "#/system/reflection/get-runtime-type.ts";
import { nullableType } from "#/system/reflection/nullable-type.ts";
import { Int32Type, StringType } from "#/system/reflection/primitive-types.ts";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { List } from "#/system/collections/list.ts";
import { Queue } from "#/system/collections/queue.ts";
import { opaqueType } from "#/system/reflection/opaque-type.ts";
import { tupleType } from "#/system/reflection/tuple-type.ts";
import { ObjectType, UnknownType } from "#/system/reflection/well-known-types.ts";

describe("H/J closed factory slots", () => {
  test("empty specializations keep their slots and per-type singleton identity", () => {
    const cases = [
      [ImmutableList.empty([Int32Type]), immutableListType(Int32Type), immutableListType(StringType)],
      [ImmutableArray.empty([Int32Type]), immutableArrayType(Int32Type), immutableArrayType(StringType)],
      [ImmutableHashSet.empty([Int32Type]), immutableHashSetType(Int32Type), immutableHashSetType(StringType)],
      [ReadOnlyList.empty([Int32Type]), readOnlyListType(Int32Type), readOnlyListType(StringType)],
      [ImmutableDictionary.empty([StringType, Int32Type]), immutableDictionaryType(StringType, Int32Type), immutableDictionaryType(StringType, StringType)],
    ] as const;

    for (const [value, matching, different] of cases) {
      expect(matching.test(value)).toBe(true);
      expect(different.test(value)).toBe(false);
      expect(getRuntimeType(value)).toBe(matching);
    }

    expect(ImmutableList.empty([Int32Type])).toBe(ImmutableList.empty([Int32Type]));
    expect(ImmutableArray.empty([Int32Type])).toBe(ImmutableArray.empty([Int32Type]));
    expect(ImmutableHashSet.empty([Int32Type])).toBe(ImmutableHashSet.empty([Int32Type]));
    expect(ImmutableDictionary.empty([StringType, Int32Type])).toBe(ImmutableDictionary.empty([StringType, Int32Type]));
    expect(ReadOnlyList.empty([Int32Type])).toBe(ReadOnlyList.empty([Int32Type]));
  });

  test("builder factories close exactly like collection factories", () => {
    const cases = [
      [new ImmutableListBuilder(null, [Int32Type]), immutableListBuilderType(Int32Type), immutableListBuilderType(StringType)],
      [new ImmutableArrayBuilder(null, [Int32Type]), immutableArrayBuilderType(Int32Type), immutableArrayBuilderType(StringType)],
      [new ImmutableHashSetBuilder(null, null, [Int32Type]), immutableHashSetBuilderType(Int32Type), immutableHashSetBuilderType(StringType)],
      [new ImmutableDictionaryBuilder(null, null, null, [StringType, Int32Type]), immutableDictionaryBuilderType(StringType, Int32Type), immutableDictionaryBuilderType(StringType, StringType)],
    ] as const;

    for (const [value, matching, different] of cases) {
      expect(matching.test(value)).toBe(true);
      expect(different.test(value)).toBe(false);
      expect(getRuntimeType(value)).toBe(matching);
    }
  });
});

describe("sorted-class descriptors", () => {
  test("factories are memoized", () => {
    expect(sortedSetType(Int32Type)).toBe(sortedSetType(Int32Type));

    expect(sortedDictionaryType(StringType, Int32Type)).toBe(
      sortedDictionaryType(StringType, Int32Type),
    );

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
    expect(String(SortedSet.of([1], null, [Int32Type]))).toBe(
      "System.Collections.Generic.SortedSet`1[System.Int32]",
    );

    expect(String(new SortedSet())).toBe("System.Collections.Generic.SortedSet`1[T]");

    expect(String(new SortedDictionary(null, null, [Int32Type, Int32Type]))).toBe(
      "System.Collections.Generic.SortedDictionary`2[System.Int32,System.Int32]",
    );

    expect(String(new SortedList())).toBe("System.Collections.Generic.SortedList`2[TKey,TValue]");
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

describe("covariant interface tests need a reference conversion (spec 011 D31)", () => {
  const readOnlyObjects = iReadOnlyListType(ObjectType);

  test("a List<int> is not an IReadOnlyList<object>", () => {
    expect(readOnlyObjects.test(new List<number>(null, [Int32Type]))).toBe(false);
  });

  test("a List<string> is an IReadOnlyList<object>", () => {
    expect(readOnlyObjects.test(new List<string>(null, [StringType]))).toBe(true);
  });

  test("a List<int> is not an IReadOnlyCollection<int?>", () => {
    const nullableInts = iReadOnlyCollectionType(nullableType(Int32Type));

    expect(nullableInts.test(new List<number>(null, [Int32Type]))).toBe(false);
  });

  test("a List<int> is an IReadOnlyList<int>, and a slotless list matches any argument", () => {
    expect(iReadOnlyListType(Int32Type).test(new List<number>(null, [Int32Type]))).toBe(true);
    expect(readOnlyObjects.test(new List<number>())).toBe(true);
  });

  test("a closed class kind compares by identity", () => {
    expect(listType(StringType).test(new List<number>(null, [Int32Type]))).toBe(false);
    expect(listType(StringType).test(new List<string>(null, [StringType]))).toBe(true);
  });
});

describe("class literals of the 011h classes (contracts/runtime-collections.md, normative for D31)", () => {
  const list = [
    ["IImmutableList", [0]],
    ["IReadOnlyList", [0]],
    ["IReadOnlyCollection", [0]],
    ["IList", [0]],
    ["ICollection", [0]],
  ];

  const listBuilder = [
    ["IList", [0]],
    ["ICollection", [0]],
    ["IReadOnlyList", [0]],
    ["IReadOnlyCollection", [0]],
  ];

  const dictionary = [
    ["IDictionary", [0, 1]],
    ["IReadOnlyDictionary", [0, 1]],
    ["ICollection", null],
    ["IReadOnlyCollection", null],
  ];

  const table = [
    [ReadOnlyList, listBuilder],
    [ImmutableList, list],
    [ImmutableArray, list],
    [
      ImmutableHashSet,
      [
        ["IImmutableSet", [0]],
        ["IReadOnlySet", [0]],
        ["IReadOnlyCollection", [0]],
        ["ISet", [0]],
        ["ICollection", [0]],
      ],
    ],
    [
      ImmutableDictionary,
      [
        ["IImmutableDictionary", [0, 1]],
        ["IReadOnlyDictionary", [0, 1]],
        ["IDictionary", [0, 1]],
        ["ICollection", null],
        ["IReadOnlyCollection", null],
      ],
    ],
    [ImmutableListBuilder, listBuilder],
    [ImmutableArrayBuilder, listBuilder],
    [
      ImmutableHashSetBuilder,
      [
        ["ISet", [0]],
        ["ICollection", [0]],
        ["IReadOnlyCollection", [0]],
      ],
    ],
    [
      ImmutableDictionaryBuilder,
      [
        ["IDictionary", [0, 1]],
        ["IReadOnlyDictionary", [0, 1]],
        ["ICollection", null],
        ["IReadOnlyCollection", null],
      ],
    ],
    [
      SortedSet,
      [
        ["ISet", [0]],
        ["ICollection", [0]],
        ["IReadOnlySet", [0]],
        ["IReadOnlyCollection", [0]],
      ],
    ],
    [SortedDictionary, dictionary],
    [SortedList, dictionary],
    [ConcurrentDictionary, dictionary],
    [
      SortedListView,
      [
        ["IList", [0]],
        ["ICollection", [0]],
      ],
    ],
  ] as const;

  test.each(
    table,
  )("%p lists .NET 10's generic collection interfaces in order", (type, expected) => {
    const entries: unknown = type.$runtimeType.interfaces?.map((entry) => [
      entry.collection,
      entry.arguments,
    ]);

    expect(entries).toEqual(expected.map(([collection, args]) => [collection, args]));
  });

  test("an immutable list answers the mutable interfaces as .NET's `is IList<T>` does", () => {
    const strings = ImmutableList.of(["a"], [StringType]);
    const array = ImmutableArray.of(["a"], [StringType]);

    expect(iListType(StringType).test(strings)).toBe(true);
    expect(iCollectionType(StringType).test(array)).toBe(true);
    expect(iImmutableListType(StringType).test(array)).toBe(true);
    expect(iSetType(Int32Type).test(ImmutableHashSet.of([1], null, [Int32Type]))).toBe(true);
    expect(iDictionaryType(StringType, Int32Type).test(ImmutableDictionary.empty())).toBe(true);
    expect(immutableArrayType(StringType).test(array)).toBe(true);
    expect(immutableArrayType(StringType).test(strings)).toBe(false);
  });

  test("a SortedList view is an IList of the list's key or value type, never read-only", () => {
    const list = SortedList.of([["a", 1]], null, [StringType, Int32Type]);

    expect(iListType(StringType).test(list.keys)).toBe(true);
    expect(iListType(Int32Type).test(list.values)).toBe(true);
    expect(iListType(Int32Type).test(list.keys)).toBe(false);
    expect(iReadOnlyListType(StringType).test(list.keys)).toBe(false);
    expect(Object.keys(list.keys)).toEqual([]);
  });

  test("#711: tuple invariance, ImmutableArray invariance, nested collection covariance, opaqueType, and Dictionary views", () => {
    const tuples = List.of([["a", 1]], [tupleType([StringType, Int32Type])]);
    expect(iReadOnlyListType(tupleType([ObjectType, Int32Type])).test(tuples)).toBe(false);
    expect(iReadOnlyListType(tupleType([StringType, Int32Type])).test(tuples)).toBe(true);

    const immutables = List.of([ImmutableArray.of(["a"], [StringType])], [immutableArrayType(StringType)]);
    expect(iReadOnlyListType(iReadOnlyListType(StringType)).test(immutables)).toBe(false);

    const nested = List.of([List.of(["a"], [StringType])], [listType(StringType)]);
    expect(iReadOnlyListType(iReadOnlyListType(ObjectType)).test(nested)).toBe(true);
    expect(iReadOnlyListType(iReadOnlyListType(Int32Type)).test(nested)).toBe(false);

    const opaqueList = List.of([{}], [opaqueType("App.Hidden")]);
    expect(listType(opaqueType("App.Hidden")).test(opaqueList)).toBe(true);
    expect(listType(Int32Type).test(opaqueList)).toBe(false);
    expect(listType(opaqueType("App.Other")).test(opaqueList)).toBe(false);

    const dict = Dictionary.of([["a", 1]], null, [StringType, Int32Type]);
    expect(iReadOnlyCollectionType(StringType).test(dict.keys)).toBe(true);
    expect(iCollectionType(StringType).test(dict.keys)).toBe(true);
    expect(iReadOnlyCollectionType(Int32Type).test(dict.keys)).toBe(false);
    expect(iReadOnlyCollectionType(Int32Type).test(dict.values)).toBe(true);
    expect(iReadOnlyCollectionType(ObjectType).test(dict.values)).toBe(false);
  });
});
