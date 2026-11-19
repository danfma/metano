import { describe, expect, test } from "bun:test";
import { ImmutableDictionaryBuilder } from "#/system/collections/immutable-dictionary-builder.ts";
import { ImmutableHashSetBuilder } from "#/system/collections/immutable-hash-set-builder.ts";
import { ImmutableListBuilder } from "#/system/collections/immutable-list-builder.ts";

// Message read from .NET 10 (2026-11-19).
const MODIFIED = "Collection was modified; enumeration operation may not execute.";

describe("BuilderIterator", () => {
  test("a builder invalidates its enumeration on every mutation, a removal included", () => {
    const listBuilder = new ImmutableListBuilder([1, 2]);
    const setBuilder = new ImmutableHashSetBuilder([1, 2]);

    const dictionaryBuilder = new ImmutableDictionaryBuilder<number, number>([
      [1, 1],
      [2, 2],
    ]);

    expect(() => {
      for (const _ of listBuilder) {
        listBuilder.add(3);
      }
    }).toThrow(MODIFIED);

    expect(() => {
      for (const _ of setBuilder) {
        setBuilder.remove(2);
      }
    }).toThrow(MODIFIED);

    expect(() => {
      for (const _ of dictionaryBuilder) {
        dictionaryBuilder.remove(2);
      }
    }).toThrow(MODIFIED);
  });
});
