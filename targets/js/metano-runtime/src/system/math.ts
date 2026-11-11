/**
 * Runtime helpers for the C# `Math.Round` / `decimal.Round` family. The compiler
 * imports them via the declarative mappings under `Metano/Runtime/Math.cs` and
 * `Metano/Runtime/Decimal.cs`.
 *
 * JavaScript's `Math.round` rounds a midpoint toward +Infinity (`Math.round(-2.5)` is
 * `-2`, `Math.round(2.5)` is `3`) and takes no digit count, while C# rounds a midpoint
 * to the nearest even number by default (`Math.Round(2.5)` is `2`) and accepts both a
 * digit count and a {@link MidpointRounding} mode.
 */

import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";

/**
 * Mirror of `System.MidpointRounding`. The numeric values match the .NET enum, so a
 * value read from serialized C# data keeps its meaning.
 */
export const MidpointRounding = {
  ToEven: 0,
  AwayFromZero: 1,
  ToZero: 2,
  ToNegativeInfinity: 3,
  ToPositiveInfinity: 4,
} as const;

export type MidpointRounding = (typeof MidpointRounding)[keyof typeof MidpointRounding];

/** `Math.Round(double, int)` accepts 0 to 15 fractional digits. */
const MAX_DOUBLE_ROUNDING_DIGITS = 15;

/** `decimal.Round(decimal, int)` accepts 0 to 28 fractional digits. */
const MAX_DECIMAL_ROUNDING_DIGITS = 28;

/** The message of the `ArgumentOutOfRangeException` for a `Math.Round(double, int)` digit count. */
const DOUBLE_DIGITS_MESSAGE = "Rounding digits must be between 0 and 15, inclusive.";

/** The message of the `ArgumentOutOfRangeException` for a `decimal.Round(decimal, int)` digit count. */
const DECIMAL_DIGITS_MESSAGE = "Decimal can only round to between 0 and 28 digits of precision.";

/** Magnitude from which every double is already an integer at 15 digits of scale. */
const DOUBLE_ROUND_LIMIT = 1e16;

/** Exact powers of ten, the same table .NET scales by before rounding. */
const POWERS_OF_TEN = [
  1, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
];

/**
 * `decimal.js` rounding constants (`Decimal.ROUND_*`) indexed by
 * {@link MidpointRounding}. The values are part of the `decimal.js` public API, so the
 * runtime does not need to depend on the package to name them.
 */
const DECIMAL_JS_ROUNDING = [
  6, // ToEven → ROUND_HALF_EVEN
  4, // AwayFromZero → ROUND_HALF_UP (a midpoint moves away from zero)
  1, // ToZero → ROUND_DOWN
  3, // ToNegativeInfinity → ROUND_FLOOR
  2, // ToPositiveInfinity → ROUND_CEIL
] as const;

/** Number of {@link MidpointRounding} modes; the values run from 0 upward. */
const MIDPOINT_ROUNDING_MODE_COUNT = Object.keys(MidpointRounding).length;

/** Structural view of a `decimal.js` `Decimal`, the only operation the helper uses. */
export interface DecimalRoundable<D> {
  toDecimalPlaces(decimalPlaces: number, rounding: number): D;
}

/**
 * Rounds `value` to `digits` fractional digits, resolving a midpoint with `mode`.
 * Mirrors `System.Math.Round(double, int, MidpointRounding)` and its shorter overloads:
 * the value is scaled by `10^digits`, rounded to an integer and scaled back, the
 * algorithm .NET 10 uses. `2.675 * 100` rounds to the double `267.5`, so
 * `roundNumber(2.675, 2)` is `2.68`, as `Math.Round(2.675, 2)` is on .NET 10. `NaN`, the
 * infinities and magnitudes of `1e16` or more are returned unchanged, and the sign of a
 * zero result is preserved.
 *
 * .NET 11 rounds the exact binary value instead (`Math.Round(2.675, 2)` is `2.67`
 * there), so parity is pinned to the .NET 10 algorithm; revisit this helper when the
 * supported target framework moves past .NET 10.
 *
 * @throws ArgumentOutOfRangeException when `digits` is outside 0–15, and
 *   ArgumentException when `mode` is not a {@link MidpointRounding} value, with
 *   .NET's messages.
 */
export function roundNumber(
  value: number,
  digits = 0,
  mode: MidpointRounding = MidpointRounding.ToEven,
): number {
  assertDigits(digits, MAX_DOUBLE_ROUNDING_DIGITS, "digits", DOUBLE_DIGITS_MESSAGE);
  assertMode(mode);

  if (!(Math.abs(value) < DOUBLE_ROUND_LIMIT)) return value;

  const power = POWERS_OF_TEN[digits]!;
  const rounded = roundToInteger(value * power, mode) / power;

  return rounded === 0 ? copySignOfZero(value) : rounded;
}

/**
 * Rounds a `decimal.js` value to `digits` fractional digits, resolving a midpoint with
 * `mode`. Mirrors `System.Math.Round(decimal, int, MidpointRounding)` and
 * `decimal.Round`. `decimal.js` rounds the exact decimal value, so no binary
 * representation error is involved.
 *
 * @throws ArgumentOutOfRangeException when `digits` is outside 0–28, and
 *   ArgumentException when `mode` is not a {@link MidpointRounding} value, with
 *   .NET's messages.
 */
export function roundDecimal<D extends DecimalRoundable<D>>(
  value: D,
  digits = 0,
  mode: MidpointRounding = MidpointRounding.ToEven,
): D {
  assertDigits(digits, MAX_DECIMAL_ROUNDING_DIGITS, "decimals", DECIMAL_DIGITS_MESSAGE);
  assertMode(mode);

  return value.toDecimalPlaces(digits, DECIMAL_JS_ROUNDING[mode]);
}

function roundToInteger(value: number, mode: MidpointRounding): number {
  switch (mode) {
    case MidpointRounding.ToEven:
      return roundHalfToEven(value);
    case MidpointRounding.AwayFromZero:
      return roundHalfAwayFromZero(value);
    case MidpointRounding.ToZero:
      return Math.trunc(value);
    case MidpointRounding.ToNegativeInfinity:
      return Math.floor(value);
    case MidpointRounding.ToPositiveInfinity:
      return Math.ceil(value);
  }
}

/**
 * Banker's rounding. `value - floor(value)` is exact for every finite double, so the
 * midpoint test never suffers a rounding error of its own.
 */
function roundHalfToEven(value: number): number {
  const floor = Math.floor(value);
  const fraction = value - floor;

  if (fraction < 0.5) return floor;

  if (fraction > 0.5) return floor + 1;

  return floor % 2 === 0 ? floor : floor + 1;
}

function roundHalfAwayFromZero(value: number): number {
  const integer = Math.trunc(value);
  const fraction = value - integer;

  return Math.abs(fraction) >= 0.5 ? integer + Math.sign(fraction) : integer;
}

/** .NET keeps the sign of the input on a zero result (`Math.Round(-0.4)` is `-0`). */
function copySignOfZero(value: number): number {
  return value < 0 || Object.is(value, -0) ? -0 : 0;
}

function assertDigits(digits: number, max: number, paramName: string, message: string): void {
  if (!Number.isInteger(digits) || digits < 0 || digits > max) {
    throw new ArgumentOutOfRangeException(paramName, undefined, message);
  }
}

function assertMode(mode: number): asserts mode is MidpointRounding {
  if (!Number.isInteger(mode) || mode < 0 || mode >= MIDPOINT_ROUNDING_MODE_COUNT) {
    throw new ArgumentException(
      `The value '${mode}' is not valid for this usage of the type MidpointRounding.`,
      "mode",
    );
  }
}
