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

  test(
    "converts a TypeError to a NullReferenceException that keeps message, stack and cause",
    () => {
      const original = new TypeError("Cannot read properties of null");

      const converted = toException(original);

      expect(converted).toBeInstanceOf(NullReferenceException);
      expect(converted.message).toBe(original.message);
      expect(converted.stack).toBe(original.stack);
      expect(converted.innerException).toBe(original);
      expect(converted.cause).toBe(original);
    },
  );

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

  test("unwraps a SuppressedError to the disposal's exception", () => {
    const disposal = new InvalidOperationException("dispose");
    const body = new InvalidOperationException("body");

    expect(toException(new SuppressedError(disposal, body))).toBe(disposal);
  });

  test("unwraps a chain of SuppressedErrors through their error", () => {
    const disposal = new InvalidOperationException("inner dispose");
    const chained = new SuppressedError(new SuppressedError(disposal, "a"), "b");

    expect(toException(chained)).toBe(disposal);
  });

  test("unwraps the stand-in TypeScript's downlevel helpers build without SuppressedError", () => {
    const disposal = new InvalidOperationException("dispose");
    const standIn = Object.assign(new Error("An error was suppressed during disposal."), {
      name: "SuppressedError",
      error: disposal,
      suppressed: new Error("body"),
    });

    expect(toException(standIn)).toBe(disposal);
  });

  test("unwraps a SuppressedError whose disposal threw a plain Error", () => {
    const converted = toException(new SuppressedError(new TypeError("x"), "body"));

    expect(converted).toBeInstanceOf(NullReferenceException);
  });

  test("works with a using declaration whose body and disposal both throw", () => {
    expect.assertions(2);

    const disposal = new InvalidOperationException("dispose");

    const run = () => {
      using _resource = {
        [Symbol.dispose]: () => {
          throw disposal;
        },
      };

      throw new InvalidOperationException("body");
    };

    try {
      run();
    } catch (error) {
      expect(error).toBeInstanceOf(SuppressedError);
      expect(toException(error)).toBe(disposal);
    }
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
