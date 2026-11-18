/**
 * The `Object.prototype.toString` tag of a value (`[object Decimal]`,
 * `[object Temporal.PlainDate]`): how the runtime recognizes decimal.js and Temporal values
 * without `instanceof`, so neither library is ever imported by reflection.
 */
export function toStringTag(value: unknown): string {
  return Object.prototype.toString.call(value);
}
