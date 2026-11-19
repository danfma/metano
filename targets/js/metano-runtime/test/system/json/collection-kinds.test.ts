import { describe, expect, test } from "bun:test";
import { Dictionary } from "#/system/collections/dictionary.ts";
import { HashSet } from "#/system/collections/hash-set.ts";
import { List } from "#/system/collections/list.ts";
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
const legacySetSpec: ValueSpec<HashSet<number>> = {
  descriptor: { kind: "hashSet", element: int32 },
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

  test("hashSet collapses duplicates, with or without create", () => {
    expect(JsonSerializer.deserialize([1, 1, 2], setSpec)?.count).toBe(2);
    expect(JsonSerializer.deserialize([1, 1, 2], legacySetSpec)).toBeInstanceOf(HashSet);
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
