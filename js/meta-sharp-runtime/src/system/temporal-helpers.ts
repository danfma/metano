/**
 * Computes the day number (days since 0001-01-01) for a date.
 * Equivalent to C# DateOnly.DayNumber.
 *
 * Accepts any object with a toString() returning an ISO date string (e.g., Temporal.PlainDate).
 */
export function dayNumber(date: { toString(): string }): number {
  const ms = Date.parse(date.toString());
  const epochMs = Date.parse("0001-01-01");
  return Math.floor((ms - epochMs) / 86_400_000);
}
