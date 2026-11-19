import { describe, expect, test } from "bun:test";
import { copyIntoSorted } from "#/system/collections/copy-to.ts";

// Texts read from .NET 10.

function caught(action: () => unknown): Error {
  try {
    action();
  } catch (error) {
    return error as Error;
  }

  throw new Error("expected a throw");
}

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
