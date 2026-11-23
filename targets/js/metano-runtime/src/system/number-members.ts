/**
 * Members of the .NET numeric types that JavaScript numbers do not have
 * (`int.CompareTo`, `double.CompareTo`), with .NET's results.
 */
import { parseFloat64 } from "./number-parsing.ts";

/**
 * `int.CompareTo(int)`, `uint.CompareTo(uint)`, `double.CompareTo(double)`: `-1`, `0` or `1`.
 * For a double, `NaN` is less than every other number and equal to itself, and `-0` equals `0`.
 */
export function compareNumber(value: number, other: number): number {
  if (Number.isNaN(value)) {
    return Number.isNaN(other) ? 0 : -1;
  }

  if (Number.isNaN(other)) {
    return 1;
  }

  return value < other ? -1 : value > other ? 1 : 0;
}

/**
 * `Convert.ToDouble(string)`: `null` converts to `0`, any other text parses as
 * `double.Parse` does.
 */
export function convertStringToDouble(text: string | null | undefined): number {
  return text == null ? 0 : parseFloat64(text);
}
