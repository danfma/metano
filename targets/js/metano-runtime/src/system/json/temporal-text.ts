const PLAIN_TIME_TAG = "[object Temporal.PlainTime]";

const NANOSECONDS_PER_TICK = 100;

const TICK_FRACTION_DIGITS = 7;

interface PlainTimeLike {
  readonly millisecond: number;
  readonly microsecond: number;
  readonly nanosecond: number;
  toString(options: { fractionalSecondDigits: number; roundingMode: "trunc" }): string;
}

/**
 * The JSON text of a temporal value, as System.Text.Json writes the C# type
 * it maps (spec 009 contracts/wire-format.md, manifest row V10).
 *
 * `TimeOnly` (`Temporal.PlainTime`) is written in the `TimeSpan` "c" form:
 * `"03:04:05"` when the fraction is zero, otherwise exactly seven fraction
 * digits (`"03:04:05.6780000"`), truncated to ticks (100 ns), the precision
 * of `TimeOnly`. Every other temporal value keeps its ISO `toString()`, which
 * already matches STJ for tick-precision `DateTime` / `DateOnly` values; a
 * sub-tick `PlainDateTime` (nanoseconds a C# `DateTime` cannot hold) still
 * writes its nine fraction digits, a text STJ never produces.
 *
 * The kind is read from `Symbol.toStringTag` so a native `Temporal` and the
 * `@js-temporal/polyfill` classes are both recognized.
 */
export function temporalTextOf(value: unknown): string {
  if (Object.prototype.toString.call(value) === PLAIN_TIME_TAG) {
    return plainTimeText(value as PlainTimeLike);
  }

  return String(value);
}

function plainTimeText(time: PlainTimeLike): string {
  const fractionDigits = hasSubSecondTicks(time) ? TICK_FRACTION_DIGITS : 0;

  return time.toString({ fractionalSecondDigits: fractionDigits, roundingMode: "trunc" });
}

function hasSubSecondTicks(time: PlainTimeLike): boolean {
  const fractionNanoseconds =
    time.millisecond * 1_000_000 + time.microsecond * 1_000 + time.nanosecond;

  return fractionNanoseconds >= NANOSECONDS_PER_TICK;
}
