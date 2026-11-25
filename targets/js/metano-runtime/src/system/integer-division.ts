/**
 * Runtime helper for C#'s integer division. The compiler lowers `/` and `/=` over
 * integral operands (`int`, `long`, `uint`, `short`, `byte`, `char`, …) to
 * {@link intDiv}, because JavaScript's `/` is always floating-point: `7 / 2` is `3.5`,
 * where C# gives `3`.
 */

import { DivideByZeroException } from "./exceptions/divide-by-zero-exception.ts";
import { OverflowException } from "./exceptions/overflow-exception.ts";

const INT32_MIN = -2147483648;

/**
 * Divides `dividend` by `divisor` and truncates the quotient toward zero, as C# does
 * for integral operands (`-7 / 2` is `-3`). A `null` (or `undefined`) operand yields
 * `null`, matching C#'s lifted `int?` division.
 *
 * @throws DivideByZeroException, with .NET's message, when `divisor` is zero,
 *   where a bare `Math.trunc(dividend / divisor)` would yield `Infinity` or `NaN`.
 * @throws OverflowException for `int.MinValue / -1`, whose quotient does not fit `int`.
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

  requireInt32Division(dividend, divisor);

  // C# integers have no negative zero: `+ 0` turns the `-0` of `-1 / 3` into `0`.
  return Math.trunc(dividend / divisor) + 0;
}

/**
 * `a % b` over `int` or `uint` operands with a divisor that is not a constant:
 * the remainder with the dividend's sign, as JavaScript's `%` computes it, but
 * throwing where .NET throws instead of yielding `NaN` or `0` (#695). A `null`
 * (or `undefined`) operand yields `null`, matching C#'s lifted `int?` remainder.
 *
 * @throws DivideByZeroException, with .NET's message, when `divisor` is zero.
 * @throws OverflowException for `int.MinValue % -1`, as .NET does.
 */
export function intRem(dividend: number, divisor: number): number;
export function intRem(
  dividend: number | null | undefined,
  divisor: number | null | undefined,
): number | null;
export function intRem(
  dividend: number | null | undefined,
  divisor: number | null | undefined,
): number | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  requireInt32Division(dividend, divisor);

  // `+ 0` turns the `-0` of `-4 % 2` into `0`.
  return (dividend % divisor) + 0;
}

/** Throws what .NET throws for an `int` `/` or `%`: a zero divisor, or `int.MinValue` by `-1`. */
function requireInt32Division(dividend: number, divisor: number): void {
  if (divisor === 0) {
    throw new DivideByZeroException();
  }

  if (dividend === INT32_MIN && divisor === -1) {
    throw new OverflowException();
  }
}
