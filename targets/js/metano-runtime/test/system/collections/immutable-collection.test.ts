import { describe, expect, test } from "bun:test";
import { ImmutableCollection } from "#/system/collections/immutable-collection.ts";

describe("ImmutableCollection", () => {
  describe("add", () => {
    test("appends item and returns new array", () => {
      const src = [1, 2, 3];
      const out = ImmutableCollection.add(src, 4);
      expect(out).toEqual([1, 2, 3, 4]);
      expect(src).toEqual([1, 2, 3]); // original untouched
    });
  });

  describe("addRange", () => {
    test("appends all items from other array", () => {
      const out = ImmutableCollection.addRange([1, 2], [3, 4]);
      expect(out).toEqual([1, 2, 3, 4]);
    });
  });

  describe("insert", () => {
    test("inserts at beginning", () => {
      expect(ImmutableCollection.insert([2, 3], 0, 1)).toEqual([1, 2, 3]);
    });

    test("inserts in the middle", () => {
      expect(ImmutableCollection.insert([1, 3], 1, 2)).toEqual([1, 2, 3]);
    });

    test("inserts at end", () => {
      expect(ImmutableCollection.insert([1, 2], 2, 3)).toEqual([1, 2, 3]);
    });

    test("does not mutate original", () => {
      const src = [1, 3];
      ImmutableCollection.insert(src, 1, 2);
      expect(src).toEqual([1, 3]);
    });
  });

  describe("removeAt", () => {
    test("removes element at index", () => {
      expect(ImmutableCollection.removeAt([1, 2, 3], 1)).toEqual([1, 3]);
    });

    test("does not mutate original", () => {
      const src = [1, 2, 3];
      ImmutableCollection.removeAt(src, 0);
      expect(src).toEqual([1, 2, 3]);
    });
  });

  describe("remove", () => {
    test("removes first occurrence of item", () => {
      expect(ImmutableCollection.remove([1, 2, 3, 2], 2)).toEqual([1, 3, 2]);
    });

    test("returns original when item not found", () => {
      const src = [1, 2, 3];
      const out = ImmutableCollection.remove(src, 99);
      expect(out).toBe(src); // same reference
    });
  });

  describe("clear", () => {
    test("returns empty array", () => {
      expect(ImmutableCollection.clear()).toEqual([]);
    });
  });

  describe("set", () => {
    test("replaces element at index", () => {
      expect(ImmutableCollection.set([1, 2, 3], 1, 99)).toEqual([1, 99, 3]);
    });

    test("does not mutate original", () => {
      const src = [1, 2, 3];
      ImmutableCollection.set(src, 0, 99);
      expect(src).toEqual([1, 2, 3]);
    });
  });
});
