import { describe, expect, test } from "bun:test";
import { ArgumentOutOfRangeException } from "#/system/exceptions/index.ts";
import { defaultIfEmpty, elementAt, elementAtOrDefault, linq, toArray } from "#/system/linq/index.ts";

describe("elementAt", () => {
  test("reads the element at the index", () => {
    expect(linq([10, 20, 30], elementAt(1))).toBe(20);
  });

  test.each([-1, 3])("throws ArgumentOutOfRangeException for index %p", (index) => {
    expect(() => linq([10, 20, 30], elementAt(index))).toThrow(ArgumentOutOfRangeException);
  });

  test("returns a null element as it is", () => {
    expect(linq([1, null], elementAt(1))).toBeNull();
  });

  test("stops reading the source at the index", () => {
    let reads = 0;

    function* source(): Iterable<number> {
      for (let value = 0; value < 100; value++) {
        reads++;
        yield value;
      }
    }

    expect(linq(source(), elementAt(2))).toBe(2);
    expect(reads).toBe(3);
  });
});

describe("elementAtOrDefault", () => {
  test("reads the element at the index", () => {
    expect(linq([10, 20, 30], elementAtOrDefault(2))).toBe(30);
  });

  test("returns null for an index outside the source", () => {
    expect(linq([10], elementAtOrDefault(5))).toBeNull();
    expect(linq([10], elementAtOrDefault(-1))).toBeNull();
  });

  test("returns the default value of the element type for a miss", () => {
    expect(linq([10], elementAtOrDefault(5, 0))).toBe(0);
  });
});

describe("defaultIfEmpty", () => {
  test("keeps a non-empty source", () => {
    expect(linq([1, 2], defaultIfEmpty(9), toArray())).toEqual([1, 2]);
  });

  test("yields the default value for an empty source", () => {
    expect(linq<number, number, number[]>([], defaultIfEmpty(9), toArray())).toEqual([9]);
  });

  test("yields null for an empty source without a default value", () => {
    expect(linq<string, string | null, (string | null)[]>([], defaultIfEmpty(), toArray())).toEqual([null]);
  });
});
