/**
 * Runtime constructors for `System.DateTimeOffset` (#673). A `DateTimeOffset` lowers to a
 * `Temporal.ZonedDateTime`; these functions build one at a fixed offset from the arguments
 * .NET's constructors take, with their range and offset checks.
 */

import type { Temporal } from "@js-temporal/polyfill";
import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";
import {
  durationTicks,
  plainDateTime,
  plainDateTimeFromTicks,
  plainDateTimeTicks,
} from "./temporal-helpers.ts";

const TICKS_PER_MINUTE = 600_000_000n;

/** The largest offset a `DateTimeOffset` holds: fourteen hours. */
const MAX_OFFSET_TICKS = 504_000_000_000n;

/** `DateTime.MaxValue.Ticks`: the last tick of 9999-12-31. */
const MAX_DATE_TIME_TICKS = 3_155_378_975_999_999_999n;

const WHOLE_MINUTES_MESSAGE = "Offset must be specified in whole minutes.";
const OFFSET_RANGE_MESSAGE = "Offset must be within plus or minus 14 hours.";
const UTC_RANGE_MESSAGE =
  "The UTC time represented when the offset is applied must be between year 0 and 10,000.";

/**
 * `new DateTimeOffset(year, month, day, hour, minute, second, offset)` and the overload
 * with a millisecond before the offset.
 *
 * @throws ArgumentOutOfRangeException for a component or the offset out of range, or a
 *   UTC time outside the `DateTime` range.
 * @throws ArgumentException when the offset is not a whole number of minutes.
 */
export function dateTimeOffset(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  millisecond: number,
  offset: Temporal.Duration,
): Temporal.ZonedDateTime {
  return atOffset(plainDateTime(year, month, day, hour, minute, second, millisecond), offset);
}

/**
 * `new DateTimeOffset(dateTime, offset)`: the clock time of `dateTime` at `offset`. A
 * `DateTime` of kind UTC or local, which .NET checks against the offset, is not told apart
 * here: the `Temporal.PlainDateTime` carries no kind.
 */
export function dateTimeOffsetFromDateTime(
  dateTime: Temporal.PlainDateTime,
  offset: Temporal.Duration,
): Temporal.ZonedDateTime {
  return atOffset(dateTime, offset);
}

/** `new DateTimeOffset(ticks, offset)`: `ticks` count the clock time at `offset`. */
export function dateTimeOffsetFromTicks(
  ticks: bigint,
  offset: Temporal.Duration,
): Temporal.ZonedDateTime {
  return atOffset(plainDateTimeFromTicks(ticks), offset);
}

function atOffset(
  clock: Temporal.PlainDateTime,
  offset: Temporal.Duration,
): Temporal.ZonedDateTime {
  const offsetTicks = durationTicks(offset);

  if (offsetTicks % TICKS_PER_MINUTE !== 0n) {
    throw new ArgumentException(WHOLE_MINUTES_MESSAGE, "offset");
  }

  if (offsetTicks > MAX_OFFSET_TICKS || offsetTicks < -MAX_OFFSET_TICKS) {
    throw new ArgumentOutOfRangeException("offset", undefined, OFFSET_RANGE_MESSAGE);
  }

  const utcTicks = plainDateTimeTicks(clock) - offsetTicks;

  if (utcTicks < 0n || utcTicks > MAX_DATE_TIME_TICKS) {
    throw new ArgumentOutOfRangeException("offset", undefined, UTC_RANGE_MESSAGE);
  }

  return clock.toZonedDateTime(offsetTimeZone(offsetTicks / TICKS_PER_MINUTE));
}

/** The `+HH:MM` time zone identifier of an offset in minutes. */
function offsetTimeZone(offsetMinutes: bigint): string {
  const sign = offsetMinutes < 0n ? "-" : "+";
  const absolute = offsetMinutes < 0n ? -offsetMinutes : offsetMinutes;
  const hours = String(absolute / 60n).padStart(2, "0");
  const minutes = String(absolute % 60n).padStart(2, "0");

  return `${sign}${hours}:${minutes}`;
}
