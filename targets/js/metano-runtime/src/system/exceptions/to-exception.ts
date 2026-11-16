import { Exception } from "./exception.ts";
import { NullReferenceException } from "./null-reference-exception.ts";
import { TaskCanceledException } from "./task-canceled-exception.ts";

/**
 * The `name` of the `DOMException` a cancelled `fetch` (or any aborted signal)
 * rejects with.
 */
const ABORT_ERROR_NAME = "AbortError";

/**
 * Normalizes whatever a JavaScript `catch` caught to a `System.Exception`, as
 * the typed clauses of a C# `try` see it:
 * - an {@link Exception} passes through;
 * - a `TypeError` (what a member access on `null` throws) becomes a
 *   {@link NullReferenceException};
 * - an `AbortError` becomes a {@link TaskCanceledException};
 * - any other `Error` (a `RangeError`, a `SyntaxError`, …) is wrapped in an
 *   {@link Exception};
 * - any other thrown value becomes an {@link Exception} carrying its text.
 *
 * A wrapper keeps the original's `message` and `stack`, and holds the original
 * as its `innerException` (`cause`). A `TypeError` thrown on purpose by
 * third-party JavaScript is also reported as a `NullReferenceException`.
 */
export function toException(value: unknown): Exception {
  if (value instanceof Exception) {
    return value;
  }

  if (!(value instanceof Error)) {
    return new Exception(String(value));
  }

  const wrapper = wrap(value);

  if (value.stack !== undefined) {
    wrapper.stack = value.stack;
  }

  return wrapper;
}

function wrap(error: Error): Exception {
  if (error instanceof TypeError) {
    return new NullReferenceException(error.message, error);
  }

  if (error.name === ABORT_ERROR_NAME) {
    return new TaskCanceledException(error.message, error);
  }

  return new Exception(error.message, error);
}

/**
 * Runs a `when` filter of a `catch` clause. A filter that throws counts as
 * `false`, as in C#: the exception is dropped and the next clause is tried.
 * A filter that reads the clause's variable receives the caught value as its
 * parameter.
 */
export function tryFilter(filter: () => boolean): boolean;
export function tryFilter<T>(filter: (value: T) => boolean, value: T): boolean;
export function tryFilter<T>(filter: (value: T) => boolean, value?: T): boolean {
  try {
    return filter(value as T);
  } catch {
    return false;
  }
}
