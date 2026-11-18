/**
 * Runtime constructors for `System.TimeSpan` (#673). A `TimeSpan` lowers to a
 * `Temporal.Duration` without calendar units (see `durationFromTicks`); these functions
 * build one from the component and unit arguments .NET's constructors and `FromX` factories
 * take, with their range and argument checks. `new Temporal.Duration(0, 5, 0)` would read its
 * arguments as years, months and weeks, which is why a `TimeSpan` never goes through it.
 */

import type { Temporal } from "@js-temporal/polyfill";
import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";
import { OverflowException } from "./exceptions/overflow-exception.ts";
import { durationFromTicks } from "./temporal-helpers.ts";

/** An amount of a time unit: a `number` (the `double` and `int` overloads) or a `bigint` (`long`). */
export type TimeSpanAmount = number | bigint;

const TICKS_PER_MICROSECOND = 10n;
const TICKS_PER_MILLISECOND = 10_000n;
const TICKS_PER_SECOND = 10_000_000n;
const TICKS_PER_MINUTE = 600_000_000n;
const TICKS_PER_HOUR = 36_000_000_000n;
const TICKS_PER_DAY = 864_000_000_000n;

/** `TimeSpan.MaxValue.Ticks` and `TimeSpan.MinValue.Ticks`. */
const MAX_TICKS = 9_223_372_036_854_775_807n;
// BigInt("-…"), not a negative literal: bun keeps `-…n` in every bundle (spec 011 FR-043).
const MIN_TICKS = /* @__PURE__ */ BigInt("-9223372036854775808");

/** The most microseconds the component constructor accepts (`TimeSpan.MaxValue` in microseconds). */
const MAX_MICROSECONDS = 922_337_203_685_477_580n;

const TIME_SPAN_OVERFLOW_MESSAGE = "TimeSpan overflowed because the duration is too long.";

const NOT_A_NUMBER_MESSAGE = "TimeSpan does not accept floating point Not-a-Number values.";

/** `TimeSpan.Zero`. */
export const timeSpanZero: Temporal.Duration = /* @__PURE__ */ durationFromTicks(0n);

/**
 * `new TimeSpan(hours, minutes, seconds)`, `new TimeSpan(days, hours, minutes, seconds)`
 * and the overloads with milliseconds and microseconds: the duration of the sum of the
 * components, each of which may be negative.
 *
 * @throws ArgumentOutOfRangeException when the sum is beyond `TimeSpan.MaxValue` or
 *   `TimeSpan.MinValue`.
 */
export function timeSpanFromParts(
  days: number,
  hours: number,
  minutes: number,
  seconds: number,
  milliseconds = 0,
  microseconds = 0,
): Temporal.Duration {
  const totalSeconds =
    BigInt(days) * 86_400n + BigInt(hours) * 3_600n + BigInt(minutes) * 60n + BigInt(seconds);
  const totalMicroseconds =
    (totalSeconds * 1_000n + BigInt(milliseconds)) * 1_000n + BigInt(microseconds);

  if (totalMicroseconds > MAX_MICROSECONDS || totalMicroseconds < -MAX_MICROSECONDS) {
    throw new ArgumentOutOfRangeException(null, undefined, TIME_SPAN_OVERFLOW_MESSAGE);
  }

  return durationFromTicks(totalMicroseconds * TICKS_PER_MICROSECOND);
}

/**
 * `ticksPerUnit` ticks per unit times `amount`. A `bigint` (a `long` overload) is exact and
 * throws `ArgumentOutOfRangeException` past the `TimeSpan` range; a `number` (the `double`
 * overload, or an `int` one) is scaled in floating point and truncated toward zero, as .NET
 * does, and throws `ArgumentException` for NaN and `OverflowException` past the range.
 */
function timeSpanFromUnit(amount: TimeSpanAmount, ticksPerUnit: bigint): Temporal.Duration {
  if (typeof amount === "bigint") {
    const exact = amount * ticksPerUnit;

    if (exact > MAX_TICKS || exact < MIN_TICKS) {
      throw new ArgumentOutOfRangeException(null, undefined, TIME_SPAN_OVERFLOW_MESSAGE);
    }

    return durationFromTicks(exact);
  }

  if (Number.isNaN(amount)) {
    throw new ArgumentException(NOT_A_NUMBER_MESSAGE);
  }

  const scaled = Math.trunc(amount * Number(ticksPerUnit));

  if (!(scaled >= Number(MIN_TICKS) && scaled < Number(MAX_TICKS))) {
    throw new OverflowException(TIME_SPAN_OVERFLOW_MESSAGE);
  }

  return durationFromTicks(BigInt(scaled));
}

/** `TimeSpan.FromDays(value)`. */
export function timeSpanFromDays(days: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(days, TICKS_PER_DAY);
}

/** `TimeSpan.FromHours(value)`. */
export function timeSpanFromHours(hours: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(hours, TICKS_PER_HOUR);
}

/** `TimeSpan.FromMinutes(value)`. */
export function timeSpanFromMinutes(minutes: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(minutes, TICKS_PER_MINUTE);
}

/** `TimeSpan.FromSeconds(value)`. */
export function timeSpanFromSeconds(seconds: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(seconds, TICKS_PER_SECOND);
}

/** `TimeSpan.FromMilliseconds(value)`. */
export function timeSpanFromMilliseconds(milliseconds: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(milliseconds, TICKS_PER_MILLISECOND);
}

/** `TimeSpan.FromMicroseconds(value)`. */
export function timeSpanFromMicroseconds(microseconds: TimeSpanAmount): Temporal.Duration {
  return timeSpanFromUnit(microseconds, TICKS_PER_MICROSECOND);
}
