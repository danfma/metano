import { Temporal } from "@js-temporal/polyfill";

const referenceDate = Temporal.PlainDate.from("0001-01-01");

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
