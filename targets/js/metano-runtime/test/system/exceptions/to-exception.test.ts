import { describe, expect, test } from "bun:test";
import {
  Exception,
  InvalidOperationException,
  NullReferenceException,
  TaskCanceledException,
  toException,
  tryFilter,
} from "#/system/exceptions/index.ts";

describe("toException", () => {
  test("passes an Exception through", () => {
    const exception = new InvalidOperationException("x");

    expect(toException(exception)).toBe(exception);
  });

  test("converts a TypeError to a NullReferenceException that keeps message, stack and cause", () => {
    const original = new TypeError("Cannot read properties of null");

    const converted = toException(original);

    expect(converted).toBeInstanceOf(NullReferenceException);
    expect(converted.message).toBe(original.message);
    expect(converted.stack).toBe(original.stack);
    expect(converted.innerException).toBe(original);
    expect(converted.cause).toBe(original);
  });

  test("converts a real null dereference", () => {
    const value = null as unknown as { name: string };

    try {
      value.name.length;
    } catch (error) {
      expect(toException(error)).toBeInstanceOf(NullReferenceException);
    }
  });

  test("converts an AbortError to a TaskCanceledException", () => {
    const original = new DOMException("aborted", "AbortError");

    const converted = toException(original);

    expect(converted).toBeInstanceOf(TaskCanceledException);
    expect(converted.innerException).toBe(original as unknown as Error);
  });

  test("wraps any other Error in an Exception", () => {
    const original = new RangeError("Invalid array length");

    const converted = toException(original);

    expect(converted).toBeInstanceOf(Exception);
    expect(converted).not.toBeInstanceOf(NullReferenceException);
    expect(converted.message).toBe("Invalid array length");
    expect(converted.innerException).toBe(original);
  });

  test("turns a thrown non-Error value into an Exception carrying its text", () => {
    expect(toException("boom").message).toBe("boom");
    expect(toException(42).message).toBe("42");
    expect(toException(null).message).toBe("null");
  });
});

describe("tryFilter", () => {
  test("returns the filter's result", () => {
    expect(tryFilter(() => true)).toBe(true);
    expect(tryFilter(() => false)).toBe(false);
  });

  test("counts a filter that throws as false", () => {
    expect(
      tryFilter(() => {
        throw new Error("filter failed");
      }),
    ).toBe(false);
  });
});
