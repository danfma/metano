/**
 * The argument guards of .NET's exception classes: `ArgumentNullException.ThrowIfNull`,
 * `ArgumentException.ThrowIfNullOrEmpty` / `ThrowIfNullOrWhiteSpace` and the
 * `ArgumentOutOfRangeException.ThrowIf*` family. They throw what .NET throws, with its
 * messages. The compiler maps each guard here declaratively (`Metano/Runtime/Exceptions.cs`)
 * and passes the `[CallerArgumentExpression]` text C# supplies as `paramName`. They are
 * module functions rather than static members, so the exception classes need not import
 * each other in a cycle.
 */

import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";
import { isNullOrWhiteSpace } from "./strings.ts";

/** The message of `ArgumentException.ThrowIfNullOrEmpty` for an empty string. */
const EMPTY_STRING_MESSAGE = "The value cannot be an empty string.";

/** The message of `ArgumentException.ThrowIfNullOrWhiteSpace` for a blank string. */
const WHITE_SPACE_MESSAGE =
  "The value cannot be an empty string or composed entirely of whitespace.";

/**
 * A value `ArgumentOutOfRangeException.ThrowIf*` compares: a JavaScript number or bigint,
 * or a `decimal.js` value, which compares through `cmp`.
 */
export type ComparableNumber = number | bigint | { cmp(other: never): number };

/** Mirrors `ArgumentNullException.ThrowIfNull(argument, paramName)`. */
export function throwIfNull(argument: unknown, paramName?: string | null): void {
  if (argument == null) {
    throw new ArgumentNullException(paramName);
  }
}

/** Mirrors `ArgumentException.ThrowIfNullOrEmpty(argument, paramName)`. */
export function throwIfNullOrEmpty(
  argument: string | null | undefined,
  paramName?: string | null,
): void {
  throwIfNull(argument, paramName);

  if (argument === "") {
    throw new ArgumentException(EMPTY_STRING_MESSAGE, paramName);
  }
}

/** Mirrors `ArgumentException.ThrowIfNullOrWhiteSpace(argument, paramName)`. */
export function throwIfNullOrWhiteSpace(
  argument: string | null | undefined,
  paramName?: string | null,
): void {
  throwIfNull(argument, paramName);

  if (isNullOrWhiteSpace(argument)) {
    throw new ArgumentException(WHITE_SPACE_MESSAGE, paramName);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfZero(value, paramName)`. */
export function throwIfZero(value: ComparableNumber, paramName?: string | null): void {
  if (compareToZero(value) === 0) {
    throw outOfRange(value, paramName, "must be a non-zero value.");
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfNegative(value, paramName)`. */
export function throwIfNegative(value: ComparableNumber, paramName?: string | null): void {
  if (compareToZero(value) < 0) {
    throw outOfRange(value, paramName, "must be a non-negative value.");
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName)`. */
export function throwIfNegativeOrZero(value: ComparableNumber, paramName?: string | null): void {
  if (compareToZero(value) <= 0) {
    throw outOfRange(value, paramName, "must be a non-negative and non-zero value.");
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfEqual(value, other, paramName)`. */
export function throwIfEqual<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) === 0) {
    throw outOfRange(value, paramName, `must not be equal to '${String(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfNotEqual(value, other, paramName)`. */
export function throwIfNotEqual<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) !== 0) {
    throw outOfRange(value, paramName, `must be equal to '${String(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfGreaterThan(value, other, paramName)`. */
export function throwIfGreaterThan<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) > 0) {
    throw outOfRange(value, paramName, `must be less than or equal to '${String(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, other, paramName)`. */
export function throwIfGreaterThanOrEqual<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) >= 0) {
    throw outOfRange(value, paramName, `must be less than '${String(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfLessThan(value, other, paramName)`. */
export function throwIfLessThan<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) < 0) {
    throw outOfRange(value, paramName, `must be greater than or equal to '${String(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, other, paramName)`. */
export function throwIfLessThanOrEqual<T extends ComparableNumber>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) <= 0) {
    throw outOfRange(value, paramName, `must be greater than '${String(other)}'.`);
  }
}

/** .NET's `<paramName> ('<value>') <rule>` exception, with the value as `actualValue`. */
function outOfRange(
  value: ComparableNumber,
  paramName: string | null | undefined,
  rule: string,
): ArgumentOutOfRangeException {
  return new ArgumentOutOfRangeException(
    paramName,
    value,
    `${String(paramName)} ('${String(value)}') ${rule}`,
  );
}

/**
 * The sign of `value - other`: `NaN` when either is a `NaN` number, so every ordered
 * test is false, as .NET's comparisons are.
 */
function compare(value: ComparableNumber, other: ComparableNumber): number {
  if (typeof value === "object") {
    return value.cmp(other as never);
  }

  const primitive = other as number | bigint;

  if (value === primitive) {
    return 0;
  }

  return value < primitive ? -1 : value > primitive ? 1 : Number.NaN;
}

function compareToZero(value: ComparableNumber): number {
  return typeof value === "object"
    ? value.cmp(0 as never)
    : compare(value, typeof value === "bigint" ? 0n : 0);
}
