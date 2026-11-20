import { describe, expect, test } from "bun:test";
import { ConcurrentDictionary } from "#/system/collections/concurrent-dictionary.ts";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { ImmutableArray } from "#/system/collections/immutable-array.ts";
import { ImmutableDictionary } from "#/system/collections/immutable-dictionary.ts";
import { ImmutableHashSet } from "#/system/collections/immutable-hash-set.ts";
import { ImmutableList } from "#/system/collections/immutable-list.ts";
import { List } from "#/system/collections/list.ts";
import { ReadOnlyList } from "#/system/collections/read-only-list.ts";
import { SortedDictionary } from "#/system/collections/sorted-dictionary.ts";
import { SortedList } from "#/system/collections/sorted-list.ts";
import { SortedSet } from "#/system/collections/sorted-set.ts";
import { NotSupportedException } from "#/system/exceptions/not-supported-exception.ts";
import { Queue } from "#/system/collections/queue.ts";
import { Stack } from "#/system/collections/stack.ts";
import { JsonSerializer, type ValueSpec } from "#/system/json/index.ts";

/** The runtime collection kinds of spec 011 FR-028, with the `create` a context writes. */
const int32 = { kind: "primitive", numberType: "System.Int32" } as const;

const listSpec: ValueSpec<List<number>> = {
  descriptor: { kind: "list", element: int32, create: (items) => List.of(items) },
};

const queueSpec: ValueSpec<Queue<number>> = {
  descriptor: { kind: "queue", element: int32, create: (items) => Queue.from(items) },
};

const stackSpec: ValueSpec<Stack<number>> = {
  descriptor: { kind: "stack", element: int32, create: (items) => Stack.from(items) },
};

const setSpec: ValueSpec<HashSet<number>> = {
  descriptor: { kind: "hashSet", element: int32, create: (items) => HashSet.from(items) },
};

const dictionarySpec: ValueSpec<Dictionary<number, string>> = {
  descriptor: {
    kind: "dictionary",
    key: int32,
    value: { kind: "primitive" },
    create: (entries) => Dictionary.assign(entries as [number, string][]),
  },
};

describe("JSON collection kinds", () => {
  test("list round-trips into a List", () => {
    const read = JsonSerializer.deserialize([1, 2], listSpec);

    expect(read).toBeInstanceOf(List);
    expect(read?.toArray()).toEqual([1, 2]);
    expect(JsonSerializer.serialize(List.of([3, 4]), listSpec)).toEqual([3, 4]);
  });

  test("queue writes front to back and reads in order", () => {
    const read = JsonSerializer.deserialize([1, 2, 3], queueSpec);

    expect(read?.peek()).toBe(1);
    expect(JsonSerializer.serialize(Queue.from([1, 2, 3]), queueSpec)).toEqual([1, 2, 3]);
  });

  test("stack writes top first and reads pushing in order, as System.Text.Json", () => {
    expect(JsonSerializer.serialize(Stack.from([1, 2, 3]), stackSpec)).toEqual([3, 2, 1]);

    const read = JsonSerializer.deserialize([1, 2, 3], stackSpec);

    expect(read?.peek()).toBe(3);
    expect([...(read ?? [])]).toEqual([3, 2, 1]);
  });

  test("hashSet creates a HashSet and collapses duplicates", () => {
    const read = JsonSerializer.deserialize([1, 1, 2], setSpec);

    expect(read).toBeInstanceOf(HashSet);
    expect(read?.count).toBe(2);
  });

  test("dictionary parses keys, keeps the last of a repeated key, writes String(key)", () => {
    const read = JsonSerializer.deserialize({ "1": "a", "01": "b" }, dictionarySpec);

    expect(read).toBeInstanceOf(Dictionary);
    expect(read?.count).toBe(1);
    expect(read?.get(1)).toBe("b");

    expect(JsonSerializer.serialize(Dictionary.of([[2, "x"]]), dictionarySpec)).toEqual({
      "2": "x",
    });
  });

  test("null reads as null; a wrong JSON kind throws", () => {
    expect(JsonSerializer.deserialize(null, listSpec)).toBeNull();

    expect(() => JsonSerializer.deserialize({}, listSpec)).toThrow(
      "Expected array for deserialization, got object",
    );

    expect(() => JsonSerializer.deserialize([], dictionarySpec)).toThrow(
      "Expected object for deserialization, got array",
    );
  });
});

/** A stage 011h sequence spec, with the `create` a context writes. */
function sequenceSpec(
  kind: "immutableList" | "immutableArray" | "readOnlyList" | "immutableHashSet" | "sortedSet",
  create: (items: number[]) => Iterable<number>,
): ValueSpec<Iterable<number>> {
  return {
    descriptor: { kind, element: int32, create: (items) => create(items as number[]) },
  };
}

/** A stage 011h keyed spec, with the `create` a context writes. */
function keyedSpec(
  kind: "immutableDictionary" | "concurrentDictionary" | "sortedDictionary" | "sortedList",
  create: (entries: [number, string][]) => Iterable<readonly [number, string]>,
): ValueSpec<Iterable<readonly [number, string]>> {
  return {
    descriptor: {
      kind,
      key: int32,
      value: { kind: "primitive" },
      create: (entries) => create(entries as [number, string][]),
    },
  };
}

describe("JSON collection kinds of stage 011h", () => {
  test.each([
    [
      "immutableList",
      sequenceSpec("immutableList", (items) => ImmutableList.from(items)),
      ImmutableList,
    ],
    [
      "immutableArray",
      sequenceSpec("immutableArray", (items) => ImmutableArray.from(items)),
      ImmutableArray,
    ],
    [
      "immutableHashSet",
      sequenceSpec("immutableHashSet", (items) => ImmutableHashSet.from(items)),
      ImmutableHashSet,
    ],
    ["sortedSet", sequenceSpec("sortedSet", (items) => new SortedSet(items)), SortedSet],
  ] as const)("%s round-trips through its create", (_kind, spec, type) => {
    const read = JsonSerializer.deserialize([3, 1, 2], spec);

    expect(read).toBeInstanceOf(type);
    expect(JsonSerializer.serialize(read, spec)).toEqual([...(read ?? [])]);
    expect(JsonSerializer.deserialize(null, spec)).toBeNull();
  });

  test("sortedSet writes in order and drops duplicates", () => {
    const spec = sequenceSpec("sortedSet", (items) => new SortedSet(items));

    expect(JsonSerializer.serialize(SortedSet.of([3, 1, 3]), spec)).toEqual([1, 3]);
  });

  test("readOnlyList writes, and its create throws NotSupportedException", () => {
    const spec = sequenceSpec("readOnlyList", (): ReadOnlyList<number> => {
      throw new NotSupportedException();
    });

    expect(JsonSerializer.serialize(new ReadOnlyList([1, 2]), spec)).toEqual([1, 2]);
    expect(() => JsonSerializer.deserialize([1], spec)).toThrow(NotSupportedException);
  });

  test.each([
    [
      "immutableDictionary",
      keyedSpec("immutableDictionary", (entries) =>
        ImmutableDictionary.empty<number, string>().setItems(entries),
      ),
    ],
    [
      "concurrentDictionary",
      keyedSpec("concurrentDictionary", (entries) => ConcurrentDictionary.assign(entries)),
    ],
    [
      "sortedDictionary",
      keyedSpec("sortedDictionary", (entries) => SortedDictionary.assign(entries)),
    ],
    ["sortedList", keyedSpec("sortedList", (entries) => SortedList.assign(entries))],
  ] as const)("%s parses keys, keeps the last of a repeated key and round-trips", (_kind, spec) => {
    const read = JsonSerializer.deserialize({ "2": "b", "1": "a", "01": "c" }, spec);

    expect([...(read ?? [])].sort(([a], [b]) => a - b)).toEqual([
      [1, "c"],
      [2, "b"],
    ]);

    const written = JsonSerializer.serialize(read, spec) as Record<string, string>;

    expect(Object.entries(written).sort()).toEqual([
      ["1", "c"],
      ["2", "b"],
    ]);
  });
});
