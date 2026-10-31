/**
 * Runtime helper for C#'s integer division. The compiler lowers `/` and `/=` over
 * integral operands (`int`, `long`, `uint`, `short`, `byte`, `char`, …) to
 * {@link intDiv}, because JavaScript's `/` is always floating-point: `7 / 2` is `3.5`,
 * where C# gives `3`.
 */

/** The message .NET's `DivideByZeroException` carries by default. */
const DIVIDE_BY_ZERO_MESSAGE = "Attempted to divide by zero.";

/**
 * Divides `dividend` by `divisor` and truncates the quotient toward zero, as C# does
 * for integral operands (`-7 / 2` is `-3`). A `null` (or `undefined`) operand yields
 * `null`, matching C#'s lifted `int?` division.
 *
 * Overflow is not modelled: `int.MinValue / -1` returns `2147483648` instead of
 * throwing `OverflowException`, and a `long` quotient beyond `Number.MAX_SAFE_INTEGER`
 * loses precision, like every other `long` value mapped to `number`.
 *
 * @throws Error with .NET's `DivideByZeroException` message when `divisor` is zero,
 *   where a bare `Math.trunc(dividend / divisor)` would yield `Infinity` or `NaN`.
 */
export function intDiv(dividend: number, divisor: number): number;
export function intDiv(
  dividend: number | null | undefined,
  divisor: number | null | undefined,
): number | null;
export function intDiv(
  dividend: number | null | undefined,
  divisor: number | null | undefined,
): number | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  if (divisor === 0) {
    throw new Error(DIVIDE_BY_ZERO_MESSAGE);
  }

  // C# integers have no negative zero: `+ 0` turns the `-0` of `-1 / 3` into `0`.
  return Math.trunc(dividend / divisor) + 0;
}
