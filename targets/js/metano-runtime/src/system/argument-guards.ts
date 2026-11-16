/**
 * The argument guards of .NET's exception classes: `ArgumentNullException.ThrowIfNull`,
 * `ArgumentException.ThrowIfNullOrEmpty` / `ThrowIfNullOrWhiteSpace` and the
 * `ArgumentOutOfRangeException.ThrowIf*` family. They throw what .NET throws, with its
 * messages. The compiler maps each guard here declaratively (`Metano/Runtime/Exceptions.cs`)
 * and passes the `[CallerArgumentExpression]` text C# supplies as `paramName`; it maps the
 * comparison guards only for the `T` these functions compare as .NET does (numbers,
 * `char`, `bool`, the date and time types) and reports `MS0001` for any
 * other `T` (a `string`, whose .NET comparison is culture-sensitive, a `Guid`, a user
 * type). They are module functions rather than static members, so the exception classes
 * need not import each other in a cycle.
 */

import type { Temporal } from "@js-temporal/polyfill";
import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";
import { messageValue } from "./exceptions/message-value.ts";
import { valueEquals } from "./equality.ts";
import { isNullOrWhiteSpace } from "./strings.ts";

/** The message of `ArgumentException.ThrowIfNullOrEmpty` for an empty string. */
const EMPTY_STRING_MESSAGE = "The value cannot be an empty string.";

/** The message of `ArgumentException.ThrowIfNullOrWhiteSpace` for a blank string. */
const WHITE_SPACE_MESSAGE =
  "The value cannot be an empty string or composed entirely of whitespace.";

/** A `decimal.js` value, which compares through `cmp`. */
type DecimalValue = { cmp(other: never): number; isNegative(): boolean };

/** A Temporal value, whose class compares two of them (`Temporal.PlainDate.compare`). */
type TemporalValue =
  | Temporal.PlainDate
  | Temporal.PlainTime
  | Temporal.PlainDateTime
  | Temporal.ZonedDateTime
  | Temporal.Instant
  | Temporal.Duration;

/** The static `compare` of a Temporal value's class. */
type TemporalClass = { compare(left: TemporalValue, right: TemporalValue): number };

/**
 * A value the sign guards (`ThrowIfZero`, `ThrowIfNegative`, `ThrowIfNegativeOrZero`)
 * test: a number, a bigint, a `char` (a one-character string) or a `decimal.js` value.
 */
export type SignedValue = number | bigint | string | DecimalValue;

/**
 * A value the ordered guards compare as .NET's `IComparable<T>.CompareTo` does: a number
 * (a `NaN` is less than every other number and equal to itself), a bigint, a boolean, a
 * `char`, a `decimal.js` value or a Temporal value.
 */
export type OrderedValue = number | bigint | boolean | string | DecimalValue | TemporalValue;

/** Mirrors `ArgumentNullException.ThrowIfNull(argument, paramName)`. */
export function throwIfNull<T>(
  argument: T,
  paramName?: string | null,
): asserts argument is NonNullable<T> {
  if (argument == null) {
    throw new ArgumentNullException(paramName);
  }
}

/** Mirrors `ArgumentException.ThrowIfNullOrEmpty(argument, paramName)`. */
export function throwIfNullOrEmpty(
  argument: string | null | undefined,
  paramName?: string | null,
): asserts argument is string {
  throwIfNull(argument, paramName);

  if (argument === "") {
    throw new ArgumentException(EMPTY_STRING_MESSAGE, paramName);
  }
}

/** Mirrors `ArgumentException.ThrowIfNullOrWhiteSpace(argument, paramName)`. */
export function throwIfNullOrWhiteSpace(
  argument: string | null | undefined,
  paramName?: string | null,
): asserts argument is string {
  throwIfNull(argument, paramName);

  if (isNullOrWhiteSpace(argument)) {
    throw new ArgumentException(WHITE_SPACE_MESSAGE, paramName);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfZero(value, paramName)`; `-0` is zero, `NaN` is not. */
export function throwIfZero(value: SignedValue, paramName?: string | null): void {
  if (isZero(value)) {
    throw outOfRange(value, paramName, "must be a non-zero value.");
  }
}

/**
 * Mirrors `ArgumentOutOfRangeException.ThrowIfNegative(value, paramName)`. .NET tests the
 * sign bit (`double.IsNegative`), so `-0` and `NaN` are negative too.
 */
export function throwIfNegative(value: SignedValue, paramName?: string | null): void {
  if (isNegative(value)) {
    throw outOfRange(value, paramName, "must be a non-negative value.");
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName)`. */
export function throwIfNegativeOrZero(value: SignedValue, paramName?: string | null): void {
  if (isNegative(value) || isZero(value)) {
    throw outOfRange(value, paramName, "must be a non-negative and non-zero value.");
  }
}

/**
 * Mirrors `ArgumentOutOfRangeException.ThrowIfEqual(value, other, paramName)`, with
 * `EqualityComparer<T>.Default`: `NaN` equals `NaN`, `-0` equals `0`, and a value with an
 * `equals` method (a record, a `decimal.js` or Temporal value) is asked.
 */
export function throwIfEqual<T>(value: T, other: T, paramName?: string | null): void {
  if (valueEquals(value, other)) {
    throw outOfRange(value, paramName, `must not be equal to '${messageValue(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfNotEqual(value, other, paramName)`. */
export function throwIfNotEqual<T>(value: T, other: T, paramName?: string | null): void {
  if (!valueEquals(value, other)) {
    throw outOfRange(value, paramName, `must be equal to '${messageValue(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfGreaterThan(value, other, paramName)`. */
export function throwIfGreaterThan<T extends OrderedValue>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) > 0) {
    throw outOfRange(value, paramName, `must be less than or equal to '${messageValue(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, other, paramName)`. */
export function throwIfGreaterThanOrEqual<T extends OrderedValue>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) >= 0) {
    throw outOfRange(value, paramName, `must be less than '${messageValue(other)}'.`);
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfLessThan(value, other, paramName)`. */
export function throwIfLessThan<T extends OrderedValue>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) < 0) {
    throw outOfRange(
      value,
      paramName,
      `must be greater than or equal to '${messageValue(other)}'.`,
    );
  }
}

/** Mirrors `ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, other, paramName)`. */
export function throwIfLessThanOrEqual<T extends OrderedValue>(
  value: T,
  other: T,
  paramName?: string | null,
): void {
  if (compare(value, other) <= 0) {
    throw outOfRange(value, paramName, `must be greater than '${messageValue(other)}'.`);
  }
}

/**
 * .NET's `<paramName> ('<value>') <rule>` exception, with the value as `actualValue`; a
 * `null` parameter name leaves the name empty, as .NET does.
 */
function outOfRange(
  value: unknown,
  paramName: string | null | undefined,
  rule: string,
): ArgumentOutOfRangeException {
  return new ArgumentOutOfRangeException(
    paramName,
    value,
    `${paramName ?? ""} ('${messageValue(value)}') ${rule}`,
  );
}

function isZero(value: SignedValue): boolean {
  if (typeof value === "object") {
    return value.cmp(0 as never) === 0;
  }

  if (typeof value === "string") {
    return value.charCodeAt(0) === 0;
  }

  return value === 0 || value === 0n;
}

/** .NET tests the sign bit: `-0` (a `double` or a `decimal`) and `NaN` are negative. */
function isNegative(value: SignedValue): boolean {
  if (typeof value === "object") {
    return value.isNegative();
  }

  if (typeof value === "number") {
    return value < 0 || Object.is(value, -0) || Number.isNaN(value);
  }

  return typeof value === "bigint" && value < 0n;
}

/** The sign of `IComparable<T>.CompareTo(value, other)`, as .NET orders each kind. */
function compare(value: OrderedValue, other: OrderedValue): number {
  if (typeof value === "number") {
    return compareNumbers(value, other as number);
  }

  if (typeof value === "object") {
    return "cmp" in value
      ? value.cmp(other as never)
      : (value.constructor as unknown as TemporalClass).compare(value, other as TemporalValue);
  }

  return threeWay(value, other as bigint | boolean | string);
}

/** `double.CompareTo`: `NaN` is less than every other number and equal to itself; `-0` equals `0`. */
function compareNumbers(value: number, other: number): number {
  if (Number.isNaN(value)) {
    return Number.isNaN(other) ? 0 : -1;
  }

  if (Number.isNaN(other)) {
    return 1;
  }

  return threeWay(value, other);
}

/** `-1`, `0` or `1` as `value` is less than, equal to or greater than `other`. */
function threeWay<T extends number | bigint | boolean | string>(value: T, other: T): number {
  return value === other ? 0 : value < other ? -1 : 1;
}
