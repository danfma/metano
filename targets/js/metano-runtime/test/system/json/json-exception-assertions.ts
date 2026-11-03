import { expect } from "bun:test";
import { JsonException, NotSupportedException } from "#/system/json/index.ts";

/** Runs `action` and returns what it throws; fails when it returns. */
export function errorOf(action: () => unknown): unknown {
  try {
    action();
  } catch (error) {
    return error;
  }

  throw new Error("Expected the action to throw.");
}

/** Asserts that `action` throws a `JsonException` with exactly `message`. */
export function expectJsonException(action: () => unknown, message: string): void {
  const error = errorOf(action);

  expect(error).toBeInstanceOf(JsonException);
  expect((error as JsonException).name).toBe("JsonException");
  expect((error as JsonException).message).toBe(message);
}

/** Asserts that `action` throws a `NotSupportedException` with exactly `message`. */
export function expectNotSupportedException(action: () => unknown, message: string): void {
  const error = errorOf(action);

  expect(error).toBeInstanceOf(NotSupportedException);
  expect((error as NotSupportedException).name).toBe("NotSupportedException");
  expect((error as NotSupportedException).message).toBe(message);
}
