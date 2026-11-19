import { describe, expect, test } from "bun:test";
import { copyIntoSorted } from "#/system/collections/copy-to.ts";

import {
  requireKey,
  throwArgumentOutOfRange,
  throwCollectionTooSmall,
  throwConcurrencyLevel,
  throwConcurrentDuplicateKey,
  throwConcurrentSourceDuplicate,
  throwDuplicateKey,
  throwKeyListWrite,
  throwNegativeValue,
  throwOOR,
  throwSortedListNested,
  throwViewBoundsInverted,
} from "#/system/collections/throw-helpers.ts";

// The stage 011h rows of contracts/runtime-collections.md § Cold throw helpers
// (texts read from .NET 10).

function caught(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

describe("stage 011h throw helpers", () => {
  test("each throws its class with .NET's text", () => {
    const rows: [() => never, string, string][] = [
      [
        () => throwOOR(5),
        "ArgumentOutOfRangeException",
        "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')\nActual value was 5.",
      ],
      [
        () => throwDuplicateKey(1, "key"),
        "ArgumentException",
        "An item with the same key has already been added. Key: 1 (Parameter 'key')",
      ],
      [
        () => throwNegativeValue("capacity", -1),
        "ArgumentOutOfRangeException",
        "capacity ('-1') must be a non-negative value. (Parameter 'capacity')\nActual value was -1.",
      ],
      [
        throwCollectionTooSmall,
        "ArgumentException",
        "Destination array is not long enough to copy all the items in the collection. Check array index and length.",
      ],
      [
        throwViewBoundsInverted,
        "ArgumentException",
        "Must be less than or equal to upperValue. (Parameter 'lowerValue')",
      ],
      [
        () => throwArgumentOutOfRange("item"),
        "ArgumentOutOfRangeException",
        "Specified argument was out of the range of valid values. (Parameter 'item')",
      ],
      [
        throwSortedListNested,
        "NotSupportedException",
        "This operation is not supported on SortedList nested types because they require modifying the original SortedList.",
      ],
      [
        throwKeyListWrite,
        "NotSupportedException",
        "Mutating a key collection derived from a dictionary is not allowed.",
      ],
      [
        throwConcurrencyLevel,
        "ArgumentOutOfRangeException",
        "The concurrencyLevel argument must be positive, or -1 to indicate a default level. (Parameter 'concurrencyLevel')",
      ],
      [
        throwConcurrentDuplicateKey,
        "ArgumentException",
        "The key already existed in the dictionary.",
      ],
      [
        throwConcurrentSourceDuplicate,
        "ArgumentException",
        "The source argument contains duplicate keys.",
      ],
    ];

    for (const [action, name, message] of rows) {
      const error = caught(action);

      expect(error.name).toBe(name);
      expect(error.message).toBe(message);
    }
  });

  test("throwOOR without an index keeps List's text", () => {
    expect(caught(() => throwOOR()).message).toBe(
      "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')",
    );
  });

  test("requireKey returns the key, or throws ArgumentNullException (key)", () => {
    expect(requireKey("a")).toBe("a");
    expect(requireKey(0)).toBe(0);
    expect(caught(() => requireKey(null)).message).toBe("Value cannot be null. (Parameter 'key')");
    expect(caught(() => requireKey(undefined)).name).toBe("ArgumentNullException");
  });
});

describe("copyIntoSorted", () => {
  test("copies from the index", () => {
    const target = [0, 0, 0];

    copyIntoSorted([1, 2], target, 1);

    expect(target).toEqual([0, 1, 2]);
  });

  test("checks as the sorted classes' CopyTo", () => {
    expect(caught(() => copyIntoSorted([1], null as never, 0)).message).toBe(
      "Value cannot be null. (Parameter 'array')",
    );

    expect(caught(() => copyIntoSorted([1], [0, 0], -1)).message).toBe(
      "index ('-1') must be a non-negative value. (Parameter 'index')\nActual value was -1.",
    );

    expect(caught(() => copyIntoSorted([1, 2], [0, 0], 1)).message).toBe(
      "Destination array is not long enough to copy all the items in the collection. Check array index and length.",
    );
  });
});
