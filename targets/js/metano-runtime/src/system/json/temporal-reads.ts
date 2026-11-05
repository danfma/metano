import type { TemporalDescriptor } from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { Temporal } from "@js-temporal/polyfill";
import { cannotConvertToTypeName } from "./contract-name.ts";
import { declaredTypeName, type ErrorTypeName } from "./numeric-converters.ts";
import { temporalTextOf } from "./temporal-text.ts";

/**
 * The built-in temporal converters: reads accept exactly the text
 * System.Text.Json accepts (#485, measured on .NET 10 with a source
 * generated context) and throw its conversion error otherwise, with the
 * `errorTypeName` of the position (a property names the declaring contract,
 * a value root, element or dictionary key names the temporal type itself).
 * A temporal `RangeError` never escapes.
 *
 * - `TimeOnly` (`Temporal.PlainTime`): `h:mm[:ss[.f]]` — hours 0–23,
 *   minutes and seconds 0–59, any number of digits with leading zeros
 *   (`"3:04:05"`, `"03:04:5"`, `"00:00"`, `"003:04:05"`), no `T` prefix, no
 *   offset or `Z`, and a fraction of 1–7 digits (`03:04:05.1234567`), since
 *   `TimeOnly` is tick precise.
 * - `DateOnly` (`Temporal.PlainDate`): `yyyy-MM-dd` exactly, a valid
 *   calendar date of the years 1–9999.
 * - `DateTime` (`Temporal.PlainDateTime`): `yyyy-MM-dd` with an optional
 *   `THH:mm[:ss[.f]]` — two digit fields except the fraction, which accepts
 *   any number of digits truncated to the 7 a `DateTime` holds. `Z` and the
 *   offsetless text keep the wall time written; an explicit offset converts
 *   the instant to the system's local wall time, as System.Text.Json's
 *   `Kind=Local` read does (so the value depends on the machine's time zone).
 * - `DateTimeOffset` (`Temporal.ZonedDateTime`): the `DateTime` grammar with
 *   the offset or `Z` optional — an absent offset is the local one at that
 *   instant, as System.Text.Json assumes; each value keeps the offset it
 *   declares (`Z` as `+00:00`), read as a fixed-offset zone.
 * - `TimeSpan` (`Temporal.Duration`): `[-]h:mm[:ss[.f]]`,
 *   `[-]d.hh:mm:ss[.f]` or `[-]days` — hours 0–23, minutes and seconds
 *   0–59, fraction 1–7 digits, within the 64-bit tick range of `TimeSpan`.
 *
 * Two supersets beyond System.Text.Json's set keep what this runtime itself
 * writes readable (DECIDED-16: the `DateTimeOffset` / `TimeSpan` texts are
 * not mutually readable with STJ's, a documented divergence): a
 * `DateTimeOffset` also reads the zone-suffixed `ZonedDateTime` text this
 * runtime writes (`"…+02:00[+02:00]"`), and a `TimeSpan` also reads the
 * ISO duration (`"PT10H20M30S"`) it writes.
 *
 * A descriptor without a `typeName` (a hand-written spec) parses through its
 * `parse`, and a rejection throws the position's conversion error naming the
 * `typeName` it declares.
 */

const TIME_ONLY_PATTERN = /^(\d+):(\d+)(?::(\d+)(?:\.(\d{1,7}))?)?$/;

const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

const DATE_TIME_PATTERN =
  /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d+))?)?(Z|[+-]\d{2}(?::\d{2})?)?)?$/;

const TIME_SPAN_COLON_PATTERN = /^(\d+)(?::(\d+)(?::(\d+)(?:\.(\d{1,7}))?)?)?$/;

const TIME_SPAN_DAY_PATTERN = /^(\d+)\.(\d+):(\d+)(?::(\d+)(?:\.(\d{1,7}))?)?$/;

const TIME_ONLY_NAME = "System.TimeOnly";

const DATE_ONLY_NAME = "System.DateOnly";

const DATE_TIME_NAME = "System.DateTime";

const DATE_TIME_OFFSET_NAME = "System.DateTimeOffset";

const TIME_SPAN_NAME = "System.TimeSpan";

const MS_IN_NANOSECONDS = 1_000_000;

const US_IN_NANOSECONDS = 1_000;

const MILLISECOND_IN_NANOSECONDS = 1_000_000n;

const MINUTE_IN_NANOSECONDS = 60_000_000_000n;

/** The `TimeSpan` range in ticks: the whole signed 64-bit domain. */
const MIN_TIME_SPAN_TICKS = -(2n ** 63n);

const MAX_TIME_SPAN_TICKS = 2n ** 63n - 1n;

/** The longest digit run kept exact; a longer run is far past every bound. */
const MAX_DIGITS = 15;

/** A temporal text reader: the parsed value, or the position's `JsonException`. */
type TemporalReader = (text: string) => unknown;

/** The readers by the CLR name the descriptor's `typeName` carries. */
const READERS_BY_TYPE_NAME: Record<string, (text: string, error: ErrorTypeName) => unknown> = {
  [TIME_ONLY_NAME]: readTimeOnly,
  [DATE_ONLY_NAME]: readDateOnly,
  [DATE_TIME_NAME]: readDateTime,
  [DATE_TIME_OFFSET_NAME]: readDateTimeOffset,
  [TIME_SPAN_NAME]: readTimeSpan,
};

/**
 * The temporal converter of `descriptor`: System.Text.Json's reader when the
 * descriptor names its CLR type, else the hand-written `parse`, whose
 * rejection still throws the position's `JsonException`.
 */
export function temporalConverterOf(
  descriptor: TemporalDescriptor,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  const read = readerOf(descriptor, errorTypeName);

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return temporalTextOf(value);
    },
    deserialize: (value: unknown) => {
      // A JSON temporal is always a string: `null` on a non-nullable value
      // type and any other JSON kind are rejected as System.Text.Json
      // rejects them (#471).
      if (typeof value !== "string") {
        throw conversionError(descriptor, errorTypeName);
      }

      return read(value);
    },
  };
}

/**
 * The text of `descriptor` read as System.Text.Json reads it, throwing the
 * position's `JsonException`; the dictionary key reader of #479 asks for it
 * through `parsedOrUndefined`, which turns the throw into `undefined`.
 */
export function readTemporalText(descriptor: TemporalDescriptor, text: string): unknown {
  return readerOf(descriptor, declaredTypeName)(text);
}

function readerOf(descriptor: TemporalDescriptor, errorTypeName: ErrorTypeName): TemporalReader {
  const { typeName } = descriptor;

  if (typeName === undefined) {
    return parseOf(descriptor, errorTypeName);
  }

  const reader = READERS_BY_TYPE_NAME[typeName];

  if (reader === undefined) {
    return () => conversionError(descriptor, errorTypeName);
  }

  return (text) => reader(text, errorTypeName);
}

/** The hand-written `parse`, whose every failure reports the conversion error. */
function parseOf(descriptor: TemporalDescriptor, errorTypeName: ErrorTypeName): TemporalReader {
  return (text) => {
    try {
      return descriptor.parse!(text);
    } catch {
      throw conversionError(descriptor, errorTypeName);
    }
  };
}

function conversionError(descriptor: TemporalDescriptor, errorTypeName: ErrorTypeName) {
  return cannotConvertToTypeName(errorTypeName(descriptor.typeName ?? "Object"));
}

function reject(typeName: string, errorTypeName: ErrorTypeName): never {
  throw cannotConvertToTypeName(errorTypeName(typeName));
}

// ─── TimeOnly ────────────────────────────────────────────────────────────────

function readTimeOnly(text: string, errorTypeName: ErrorTypeName): unknown {
  const match = TIME_ONLY_PATTERN.exec(text);

  if (match === null) {
    return reject(TIME_ONLY_NAME, errorTypeName);
  }

  const hour = digitsOf(match[1]);
  const minute = digitsOf(match[2]);
  const second = digitsOf(match[3]);

  if (hour > 23 || minute > 59 || second > 59) {
    return reject(TIME_ONLY_NAME, errorTypeName);
  }

  return new Temporal.PlainTime(hour, minute, second, ...subSecondOf(match[4]));
}

// ─── DateOnly ────────────────────────────────────────────────────────────────

function readDateOnly(text: string, errorTypeName: ErrorTypeName): unknown {
  const match = DATE_ONLY_PATTERN.exec(text);

  if (match === null) {
    return reject(DATE_ONLY_NAME, errorTypeName);
  }

  return plainDateOf(match, errorTypeName, DATE_ONLY_NAME);
}

// ─── DateTime / DateTimeOffset ───────────────────────────────────────────────

function readDateTime(text: string, errorTypeName: ErrorTypeName): unknown {
  const match = DATE_TIME_PATTERN.exec(text);

  if (match === null) {
    return reject(DATE_TIME_NAME, errorTypeName);
  }

  return dateTimeOf(match, errorTypeName, DATE_TIME_NAME, plainDateTimeOf);
}

function readDateTimeOffset(text: string, errorTypeName: ErrorTypeName): unknown {
  const match = DATE_TIME_PATTERN.exec(text);

  if (match === null) {
    return writtenZonedDateTimeOf(text, errorTypeName);
  }

  return dateTimeOf(match, errorTypeName, DATE_TIME_OFFSET_NAME, zonedDateTimeOf);
}

/**
 * The zone-suffixed `ZonedDateTime` text this runtime writes (DECIDED-16):
 * `"2026-01-02T03:04:05+00:00[UTC]"`. System.Text.Json rejects it, so it is
 * the one input beyond STJ's set; a rejected zone still throws the position's
 * error.
 */
function writtenZonedDateTimeOf(text: string, errorTypeName: ErrorTypeName): unknown {
  const bracket = text.lastIndexOf("[");

  if (bracket < 0 || !text.endsWith("]") || !text.includes("T")) {
    return reject(DATE_TIME_OFFSET_NAME, errorTypeName);
  }

  try {
    return Temporal.ZonedDateTime.from(text);
  } catch {
    return reject(DATE_TIME_OFFSET_NAME, errorTypeName);
  }
}

/**
 * The date-time shared grammar: the date validates through `PlainDate` (a
 * February 30th and the year 0 reject the read), the two digit time fields
 * range check, and `build` assembles the value per kind.
 */
function dateTimeOf(
  match: RegExpExecArray,
  errorTypeName: ErrorTypeName,
  typeName: string,
  build: (match: RegExpExecArray, date: Temporal.PlainDate) => unknown,
): unknown {
  if (timeFieldsOf(match) === undefined) {
    return reject(typeName, errorTypeName);
  }

  const date = plainDateOf(match, errorTypeName, typeName);

  return build(match, date);
}

/** The hour/minute/second fields of the match, `undefined` when out of range. */
function timeFieldsOf(
  match: RegExpExecArray,
): { hour: number; minute: number; second: number } | undefined {
  if (match[4] === undefined) {
    return { hour: 0, minute: 0, second: 0 };
  }

  const hour = digitsOf(match[4]);
  const minute = digitsOf(match[5]);
  const second = digitsOf(match[6]);

  if (hour > 23 || minute > 59 || second > 59) {
    return undefined;
  }

  return { hour, minute, second };
}

/**
 * The `PlainDateTime` of a `DateTime` read: the wall time written for a
 * date-only text and for `Z`, and the system's local wall time of the
 * instant for an explicit offset, as System.Text.Json's `Kind=Local` read
 * does.
 */
function plainDateTimeOf(match: RegExpExecArray, date: Temporal.PlainDate): unknown {
  const { hour, minute, second } = timeFieldsOf(match)!;
  const subSecond = subSecondOf(match[7]);

  if (match[8] === undefined || match[8] === "Z") {
    return new Temporal.PlainDateTime(
      date.year,
      date.month,
      date.day,
      hour,
      minute,
      second,
      ...subSecond,
    );
  }

  const offsetMinutes = explicitOffsetMinutesOf(match[8]);
  const wallMs = wallMillisecondsOf(date, match);
  const local = new Date(wallMs - offsetMinutes * 60_000);

  return new Temporal.PlainDateTime(
    local.getFullYear(),
    local.getMonth() + 1,
    local.getDate(),
    local.getHours(),
    local.getMinutes(),
    local.getSeconds(),
    ...subSecond,
  );
}

/** The `ZonedDateTime` of a `DateTimeOffset` read, keeping the offset read. */
function zonedDateTimeOf(match: RegExpExecArray, date: Temporal.PlainDate): unknown {
  const declaredOffset = match[8] === undefined ? undefined : explicitOffsetMinutesOf(match[8]);
  const wallMs = wallMillisecondsOf(date, match);

  // An offsetless read is the local one at that instant, as System.Text.Json
  // assumes; `Z` and the explicit offsets are kept as written.
  const offsetMinutes = declaredOffset ?? -new Date(wallMs).getTimezoneOffset();

  const epochNanoseconds =
    BigInt(wallMs) * MILLISECOND_IN_NANOSECONDS -
    BigInt(offsetMinutes) * MINUTE_IN_NANOSECONDS +
    BigInt(subSecondNanosecondsOf(match[7]));

  return new Temporal.ZonedDateTime(epochNanoseconds, offsetTextOf(offsetMinutes));
}

/** The wall time's epoch milliseconds. */
function wallMillisecondsOf(date: Temporal.PlainDate, match: RegExpExecArray): number {
  const { hour, minute, second } = timeFieldsOf(match)!;

  return Date.UTC(date.year, date.month - 1, date.day, hour, minute, second);
}

/** The minutes of an explicit `±HH[:mm]` offset or `Z` (`0`). */
function explicitOffsetMinutesOf(text: string): number {
  if (text === "Z") {
    return 0;
  }

  const hours = digitsOf(text.slice(1, 3));
  const minutes = text.length > 3 ? digitsOf(text.slice(4, 6)) : 0;
  const sign = text.startsWith("-") ? -1 : 1;

  return sign * (hours * 60 + minutes);
}

/** `±HH:MM`, the fixed-offset zone a read `DateTimeOffset` keeps. */
function offsetTextOf(offsetMinutes: number): string {
  const sign = offsetMinutes < 0 ? "-" : "+";
  const absolute = Math.abs(offsetMinutes);

  return `${sign}${String(Math.floor(absolute / 60)).padStart(2, "0")}:${String(absolute % 60).padStart(2, "0")}`;
}

// ─── TimeSpan ────────────────────────────────────────────────────────────────

/**
 * The `TimeSpan` grammar: an optional sign, then the hours form
 * (`h:mm[:ss[.f]]`), the days form (`d.hh:mm:ss[.f]`) or a bare day count —
 * what System.Text.Json's invariant parse accepts, with every field range
 * checked and the whole within `TimeSpan`'s 64-bit ticks.
 */
function readTimeSpan(text: string, errorTypeName: ErrorTypeName): unknown {
  // The ISO duration this runtime writes (DECIDED-16) reads too.
  if (/^-?P/.test(text)) {
    return writtenDurationOf(text, errorTypeName);
  }

  const negative = text.startsWith("-");
  const body = negative ? text.slice(1) : text;
  const dayForm = TIME_SPAN_DAY_PATTERN.exec(body);

  const fields =
    dayForm !== null
      ? {
          days: digitsOf(dayForm[1]),
          hours: digitsOf(dayForm[2]),
          minutes: digitsOf(dayForm[3]),
          seconds: digitsOf(dayForm[4]),
          fraction: dayForm[5],
        }
      : colonFieldsOf(body);

  if (fields === undefined) {
    return reject(TIME_SPAN_NAME, errorTypeName);
  }

  if (fields.hours > 23 || fields.minutes > 59 || fields.seconds > 59) {
    return reject(TIME_SPAN_NAME, errorTypeName);
  }

  const totalNanoseconds = BigInt(negative ? -1 : 1) * nanosecondsOf(fields);

  const ticks = totalNanoseconds / 100n;

  if (ticks < MIN_TIME_SPAN_TICKS || ticks > MAX_TIME_SPAN_TICKS) {
    return reject(TIME_SPAN_NAME, errorTypeName);
  }

  const sign = negative ? -1 : 1;
  const [ms, µs, ns] = subSecondOf(fields.fraction);

  return new Temporal.Duration(
    0,
    0,
    0,
    sign * fields.days,
    sign * fields.hours,
    sign * fields.minutes,
    sign * fields.seconds,
    sign * ms,
    sign * µs,
    sign * ns,
  );
}

/** The fields of the hours form — `h:mm[:ss[.f]]` or a bare day count — or `undefined`. */
function colonFieldsOf(
  body: string,
):
  | { days: number; hours: number; minutes: number; seconds: number; fraction: string | undefined }
  | undefined {
  const colonForm = TIME_SPAN_COLON_PATTERN.exec(body);

  if (colonForm === null) {
    return undefined;
  }

  return colonForm[2] === undefined
    ? {
        // A bare day count: `10` reads as ten days.
        days: digitsOf(colonForm[1]),
        hours: 0,
        minutes: 0,
        seconds: 0,
        fraction: undefined,
      }
    : {
        days: 0,
        hours: digitsOf(colonForm[1]),
        minutes: digitsOf(colonForm[2]),
        seconds: digitsOf(colonForm[3]),
        fraction: colonForm[4],
      };
}

/**
 * The ISO duration this runtime writes (`"PT10H20M30S"`, DECIDED-16):
 * System.Text.Json rejects it, so it is the one input beyond STJ's set; a
 * rejected duration still throws the position's error.
 */
function writtenDurationOf(text: string, errorTypeName: ErrorTypeName): unknown {
  try {
    return Temporal.Duration.from(text);
  } catch {
    return reject(TIME_SPAN_NAME, errorTypeName);
  }
}

/** The nanoseconds of the fields, as a `bigint` (a `TimeSpan` overflows a `number`). */
function nanosecondsOf(fields: {
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
  fraction: string | undefined;
}): bigint {
  const fractionNanoseconds = BigInt(subSecondNanosecondsOf(fields.fraction));

  return (
    BigInt(fields.days) * DAY_IN_NANOSECONDS +
    BigInt(fields.hours) * 3_600_000_000_000n +
    BigInt(fields.minutes) * MINUTE_IN_NANOSECONDS +
    BigInt(fields.seconds) * SECOND_IN_NANOSECONDS +
    fractionNanoseconds
  );
}

const SECOND_IN_NANOSECONDS = 1_000_000_000n;

const DAY_IN_NANOSECONDS = 86_400_000_000_000n;

// ─── Shared text pieces ──────────────────────────────────────────────────────

/**
 * A `PlainDate` of a date-only or date-time match: the digit shapes are in
 * range by the patterns, and an invalid calendar date (a February 30th, the
 * year 0) rejects the read, as System.Text.Json rejects it.
 */
function plainDateOf(
  match: RegExpExecArray,
  errorTypeName: ErrorTypeName,
  typeName: string,
): Temporal.PlainDate {
  const year = digitsOf(match[1]);
  const month = digitsOf(match[2]);
  const day = digitsOf(match[3]);

  if (year < 1) {
    return reject(typeName, errorTypeName);
  }

  try {
    return new Temporal.PlainDate(year, month, day);
  } catch {
    return reject(typeName, errorTypeName);
  }
}

/** The `[ms, µs, ns]` of a fraction text, all `0` without one. */
function subSecondOf(fraction: string | undefined): [number, number, number] {
  const nanoseconds = subSecondNanosecondsOf(fraction);

  return [
    Math.floor(nanoseconds / MS_IN_NANOSECONDS),
    Math.floor(nanoseconds / US_IN_NANOSECONDS) % 1_000,
    nanoseconds % 1_000,
  ];
}

/**
 * The nanoseconds of a fraction text (`"5"` is half a second), `0` without
 * one, aligned down to the 100 ns a `DateTime` tick holds: the digits past
 * the seventh are dropped, as System.Text.Json drops them.
 */
function subSecondNanosecondsOf(fraction: string | undefined): number {
  if (fraction === undefined) {
    return 0;
  }

  const nanoseconds = Number(fraction) * 10 ** (9 - fraction.length);

  return Math.floor(nanoseconds / 100) * 100;
}

/**
 * The digits of a group as a number, `0` for the group the text leaves out;
 * leading zeros are free, a longer run rejects.
 */
function digitsOf(text: string | undefined): number {
  if (text === undefined) {
    return 0;
  }

  const withoutLeadingZeros = text.replace(/^0+(?=\d)/, "");

  if (withoutLeadingZeros.length > MAX_DIGITS) {
    return Number.MAX_SAFE_INTEGER;
  }

  return Number(withoutLeadingZeros);
}
