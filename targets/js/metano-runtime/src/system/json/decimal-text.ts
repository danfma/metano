import { REAL_NUMBER_PATTERN } from "./number-from-string.ts";

/**
 * How System.Text.Json reads a number text as a `System.Decimal` (#457),
 * measured on .NET 10.0.12: a 96-bit integer and a scale of at most 28.
 *
 * - Digits are kept from the most significant one while the scale stays at
 *   or below 28 and the integer can take one more digit (it is below
 *   `⌊(2^96 − 1) / 10⌋`, or equal to it with a next digit of at most 5).
 * - The first digit left out rounds the rest: up from 6, down below 5. A 5
 *   followed only by zeros (a tie) rounds half to even, except at the 30th
 *   digit of the text or later, where it rounds up. Digits are counted from
 *   the first non-zero integer digit, or, when the integer part is zero, from
 *   the first fraction digit, so leading fraction zeros count
 *   (`1.00000000000000000000000000025` reads as
 *   `1.0000000000000000000000000003`, `0.0000000000000000000000000000025e2`
 *   as `3e-28`, `2.5e-28` as `2e-28`; measured on about 20 000 ties).
 * - A value whose scale would pass 28 reads as zero (`1e-29`, `1e-400`); one
 *   that rounds past `MaxValue` (`79228162514264337593543950335.5`) is out of
 *   range.
 */

/** `⌊(2^96 − 1) / 10⌋`: the largest integer that takes one more digit. */
const LAST_EXTENSIBLE = 7_922_816_251_426_433_759_354_395_033n;

const INTEGER_LIMIT = 2n ** 96n;

const MAX_SCALE = 28;

/** The most digits a decimal holds; a value with a larger exponent is out of range. */
const MAX_DIGITS = 29;

/** The 0-based text position from which a tie rounds up. */
const TIE_ROUNDS_UP_FROM = 29;

/** A text with at most this many significant digits and scale is held as written. */
const EXACT_DIGITS = 28;

const LEADING_ZEROS = /^0+/;

const TRAILING_ZEROS = /0+$/;

/**
 * The `System.Decimal` that STJ reads from `text`, as a text decimal.js reads
 * exactly; `undefined` when the text is not a number of the decimal grammar
 * (FR-048 item 4) or rounds past `MaxValue`. A text the decimal holds as
 * written is returned unchanged.
 */
export function readDecimalText(text: string): string | undefined {
  const parts = REAL_NUMBER_PATTERN.exec(text);

  if (parts === null) {
    return undefined;
  }

  const { integer = "", fraction = "", bareFraction = "", exponent = "0" } = parts.groups ?? {};
  const fractionDigits = fraction + bareFraction;
  const withLeading = (integer + fractionDigits).replace(LEADING_ZEROS, "");
  const digits = withLeading.replace(TRAILING_ZEROS, "");
  const integerIsZero = integer.replace(LEADING_ZEROS, "") === "";
  const leadingFractionZeros = integerIsZero
    ? fractionDigits.length - fractionDigits.replace(LEADING_ZEROS, "").length
    : 0;
  const negative = text.charCodeAt(0) === 45;

  if (digits === "") {
    return negative ? "-0" : "0";
  }

  // The decimal point sits `pointPosition` digits after the first significant one.
  const pointPosition = withLeading.length - fractionDigits.length + Number(exponent);
  const scale = digits.length - pointPosition;

  if (digits.length <= EXACT_DIGITS && pointPosition <= EXACT_DIGITS && scale <= MAX_SCALE) {
    return text;
  }

  const rounded = roundToDecimal(digits, pointPosition, leadingFractionZeros);

  if (rounded === undefined) {
    return undefined;
  }

  return negative && rounded !== "0" ? `-${rounded}` : rounded;
}

/** Whether System.Text.Json reads `text` as a `System.Decimal`. */
export function isDecimalText(text: string): boolean {
  return readDecimalText(text) !== undefined;
}

/**
 * The magnitude `0.<digits> × 10^pointPosition` held as a decimal, in plain
 * notation; `undefined` when it is out of range. `digits` has no leading or
 * trailing zeros; `leadingFractionZeros` precede them in the text.
 */
function roundToDecimal(
  digits: string,
  pointPosition: number,
  leadingFractionZeros: number,
): string | undefined {
  if (pointPosition > MAX_DIGITS) {
    return undefined;
  }

  let integer = 0n;
  let kept = 0;
  let exponent = pointPosition;

  const digitAt = (index: number) => (index < digits.length ? digits.charCodeAt(index) - 48 : 0);

  const takesDigit = () =>
    (exponent > 0 || (kept < digits.length && exponent > -MAX_SCALE)) &&
    (integer < LAST_EXTENSIBLE || (integer === LAST_EXTENSIBLE && digitAt(kept) <= 5));

  // Past the last digit, the integer takes zeros up to the decimal point.
  while (takesDigit()) {
    integer = integer * 10n + BigInt(digitAt(kept));
    kept += kept < digits.length ? 1 : 0;
    exponent--;
  }

  if (kept < digits.length && roundsUp(digits, kept, leadingFractionZeros, integer)) {
    integer++;

    if (integer === INTEGER_LIMIT) {
      integer = LAST_EXTENSIBLE + 1n;
      exponent++;
    }
  }

  if (exponent > 0) {
    return undefined;
  }

  return exponent <= -MAX_DIGITS ? "0" : plainTextOf(integer, -exponent);
}

/**
 * Whether the digit at `next`, the first one left out, rounds `integer` up;
 * it sits at `leadingFractionZeros + next` in the text.
 */
function roundsUp(
  digits: string,
  next: number,
  leadingFractionZeros: number,
  integer: bigint,
): boolean {
  const digit = digits.charCodeAt(next) - 48;

  if (digit !== 5) {
    return digit > 5;
  }

  // `digits` has no trailing zeros, so a 5 followed only by zeros is its last digit.
  const isTie = next === digits.length - 1;
  const isPastTwentyNinthDigit = leadingFractionZeros + next >= TIE_ROUNDS_UP_FROM;

  return !isTie || isPastTwentyNinthDigit || integer % 2n === 1n;
}

/** `integer × 10^-scale` in plain notation. */
function plainTextOf(integer: bigint, scale: number): string {
  if (integer === 0n) {
    return "0";
  }

  const text = integer.toString().padStart(scale + 1, "0");
  const point = text.length - scale;

  return scale === 0 ? text : `${text.slice(0, point)}.${text.slice(point)}`;
}
