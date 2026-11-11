import { Temporal } from "@js-temporal/polyfill";
import { ArgumentOutOfRangeException } from "./exceptions/index.ts";

const referenceDate = Temporal.PlainDate.from("0001-01-01");

/** Nanoseconds per .NET tick (100 ns). */
const NANOSECONDS_PER_TICK = 100n;

/** Ticks per day: `TimeSpan.TicksPerDay`. */
const TICKS_PER_DAY = 864_000_000_000n;

/** `DateTime.MaxValue.Ticks`: the last tick of 9999-12-31. */
const MAX_DATE_TIME_TICKS = 3_155_378_975_999_999_999n;

/** .NET's message for `new DateTime(ticks)` outside the `DateTime` range. */
const DATE_TIME_TICKS_MESSAGE =
  "Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks.";

/** .NET's message for a date whose year, month and day do not exist. */
const UNREPRESENTABLE_DATE_MESSAGE =
  "Year, Month, and Day parameters describe an un-representable DateTime.";

/** .NET's message for a time whose hour, minute and second do not exist. */
const UNREPRESENTABLE_TIME_MESSAGE =
  "Hour, Minute, and Second parameters describe an un-representable DateTime.";

/** .NET's message for a millisecond outside 0–999. */
const MILLISECOND_MESSAGE = "Valid values are between 0 and 999, inclusive.";

/** The time units of a duration, from days down to nanoseconds, with their size in nanoseconds. */
const TIME_UNITS = [
  ["days", 86_400_000_000_000n],
  ["hours", 3_600_000_000_000n],
  ["minutes", 60_000_000_000n],
  ["seconds", 1_000_000_000n],
  ["milliseconds", 1_000_000n],
  ["microseconds", 1_000n],
  ["nanoseconds", 1n],
] as const;

/**
 * Computes the day number (days since 0001-01-01) for a date.
 * Equivalent to C# DateOnly.DayNumber.
 *
 * Accepts any object with a toString() returning an ISO date string (e.g., Temporal.PlainDate).
 */
export function dayNumber(date: Temporal.PlainDate | { toString(): string }): number {
  const current = Temporal.PlainDate.from(
    date instanceof Temporal.PlainDate ? date : date.toString(),
  );

  return referenceDate.until(current, { largestUnit: "days" }).days;
}

/**
 * `TimeSpan.Ticks`: the duration in 100 ns ticks, truncated toward zero.
 * A `TimeSpan` maps to a duration without calendar units (days and smaller).
 */
export function durationTicks(duration: Temporal.Duration): bigint {
  const nanoseconds = TIME_UNITS.reduce(
    (total, [unit, size]) => total + BigInt(duration[unit]) * size,
    0n,
  );

  return nanoseconds / NANOSECONDS_PER_TICK;
}

/** `TimeSpan.FromTicks(ticks)` and `new TimeSpan(ticks)`: a duration balanced from days down to nanoseconds. */
export function durationFromTicks(ticks: bigint): Temporal.Duration {
  const sign = ticks < 0n ? -1n : 1n;
  const nanoseconds = ticks * NANOSECONDS_PER_TICK * sign;
  const fields = Object.fromEntries(
    TIME_UNITS.map(([unit, size], index) => {
      const larger = index === 0 ? null : TIME_UNITS[index - 1]?.[1];
      const withinLarger = larger == null ? nanoseconds : nanoseconds % larger;

      return [unit, Number((withinLarger / size) * sign)];
    }),
  );

  return Temporal.Duration.from(fields);
}

/** `DateTime.Ticks`: the 100 ns ticks since 0001-01-01T00:00:00. */
export function plainDateTimeTicks(value: Temporal.PlainDateTime): bigint {
  const days = BigInt(dayNumber(value.toPlainDate()));
  const timeOfDay = value.toPlainTime().since(Temporal.PlainTime.from("00:00"));

  return days * TICKS_PER_DAY + durationTicks(timeOfDay);
}

/**
 * `new DateTime(ticks)`: the date and time `ticks` 100 ns ticks after 0001-01-01T00:00:00.
 *
 * @throws ArgumentOutOfRangeException outside `DateTime.MinValue.Ticks` … `DateTime.MaxValue.Ticks`.
 */
export function plainDateTimeFromTicks(ticks: bigint): Temporal.PlainDateTime {
  if (ticks < 0n || ticks > MAX_DATE_TIME_TICKS) {
    throw new ArgumentOutOfRangeException("ticks", null, DATE_TIME_TICKS_MESSAGE);
  }

  const date = referenceDate.add({ days: Number(ticks / TICKS_PER_DAY) });
  const timeOfDay = durationFromTicks(ticks % TICKS_PER_DAY);

  return date.toPlainDateTime().add(timeOfDay);
}

/**
 * `new DateOnly(year, month, day)`: the `Temporal.PlainDate`, or .NET's
 * `ArgumentOutOfRangeException` for a date outside 0001-01-01–9999-12-31 or one
 * that does not exist, where the Temporal constructor throws a `RangeError`.
 */
export function plainDate(year: number, month: number, day: number): Temporal.PlainDate {
  if (!isRepresentableDate(year, month, day)) {
    throw new ArgumentOutOfRangeException(null, undefined, UNREPRESENTABLE_DATE_MESSAGE);
  }

  return new Temporal.PlainDate(year, month, day);
}

/**
 * `new TimeOnly(hour, minute, second, millisecond)`: the `Temporal.PlainTime`, or
 * .NET's `ArgumentOutOfRangeException` for a component out of range.
 */
export function plainTime(
  hour: number,
  minute: number,
  second = 0,
  millisecond = 0,
): Temporal.PlainTime {
  assertTime(hour, minute, second, millisecond);

  return new Temporal.PlainTime(hour, minute, second, millisecond);
}

/**
 * `new DateTime(year, month, day, hour, minute, second, millisecond)`: the
 * `Temporal.PlainDateTime`, or .NET's `ArgumentOutOfRangeException` for a
 * component out of range.
 */
export function plainDateTime(
  year: number,
  month: number,
  day: number,
  hour = 0,
  minute = 0,
  second = 0,
  millisecond = 0,
): Temporal.PlainDateTime {
  assertTime(hour, minute, second, millisecond);

  if (!isRepresentableDate(year, month, day)) {
    throw new ArgumentOutOfRangeException(null, undefined, UNREPRESENTABLE_DATE_MESSAGE);
  }

  return new Temporal.PlainDateTime(year, month, day, hour, minute, second, millisecond);
}

function assertTime(hour: number, minute: number, second: number, millisecond: number): void {
  if (!isInRange(millisecond, 999)) {
    throw new ArgumentOutOfRangeException("millisecond", undefined, MILLISECOND_MESSAGE);
  }

  if (!isInRange(hour, 23) || !isInRange(minute, 59) || !isInRange(second, 59)) {
    throw new ArgumentOutOfRangeException(null, undefined, UNREPRESENTABLE_TIME_MESSAGE);
  }
}

function isRepresentableDate(year: number, month: number, day: number): boolean {
  if (!isInRange(year, 9999) || year < 1 || !isInRange(month, 12) || month < 1 || day < 1) {
    return false;
  }

  return Number.isInteger(day) && day <= new Temporal.PlainYearMonth(year, month).daysInMonth;
}

function isInRange(value: number, max: number): boolean {
  return Number.isInteger(value) && value >= 0 && value <= max;
}
