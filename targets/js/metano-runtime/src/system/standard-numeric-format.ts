/**
 * The shared core of .NET's standard numeric format strings in the invariant
 * culture (`F2`, `N0`, `E3`, `G5`, `P`, `C`, …), used by the integer and the
 * floating-point formatters. A value reaches it as its decimal digits, so
 * each formatter decides how those digits are produced and rounded: an
 * integer's digits are exact and round half away from zero, a double's are
 * the exact expansion of its binary value and round half to even, as .NET
 * Core 3.0+ does. Not re-exported from the package root: the formatters are
 * the public surface.
 */
import { FormatException, NotSupportedException } from "./exceptions/index.ts";

/**
 * A finite value as `±0.digits × 10^scale`: `digits` has neither leading nor
 * trailing zeros, and is empty for zero. `negative` is kept for a zero too,
 * because .NET prints the sign of a negative value that rounds to zero
 * (`-0.00`).
 */
export type DecimalDigits = {
  readonly negative: boolean;
  readonly digits: string;
  readonly scale: number;
};

/** How a dropped tail of digits rounds the kept ones. */
export type Rounding = "halfAwayFromZero" | "halfToEven";

/** A standard numeric format: its letter as written and its precision, `null` without one. */
export type StandardFormat = {
  readonly letter: string;
  readonly precision: number | null;
};

/** .NET's message for a format string that is neither standard nor custom. */
const INVALID_FORMAT_MESSAGE = "Format specifier was invalid.";

/** A standard numeric format: one letter and an optional precision. */
const STANDARD_FORMAT = /^([A-Za-z])(\d*)$/;

/** The largest precision .NET accepts in a standard format. */
const MAX_PRECISION = 999_999_999;

/** The digits of a thousands group. */
const GROUP_SIZE = 3;

/** `F`, `N`, `P` and `C` print two decimals when the format gives no precision. */
const DEFAULT_DECIMALS = 2;

/** `E` prints six decimals when the format gives no precision. */
const DEFAULT_EXPONENT_DECIMALS = 6;

/** The minimum digits of an `E` exponent (`E+006`). */
const MIN_SCIENTIFIC_EXPONENT_DIGITS = 3;

/** The minimum digits of a `G` exponent (`E+06`). */
const MIN_GENERAL_EXPONENT_DIGITS = 2;

/** `G` switches to scientific notation below `0.0001`: a scale under this one. */
const MIN_GENERAL_FIXED_SCALE = -3;

/** The invariant culture's currency symbol. */
const CURRENCY_SYMBOL = "¤";

/** `P` multiplies by 100: its digits move two places. */
const PERCENT_SCALE_SHIFT = 2;

/** The smallest digit that rounds a kept digit up. */
const MIDPOINT_DIGIT = "5";

/**
 * The letter and precision of `format`, or `null` for a custom format
 * (anything but one letter and an optional precision).
 *
 * @throws FormatException for a precision above .NET's maximum.
 */
export function parseStandardFormat(format: string): StandardFormat | null {
  const standard = STANDARD_FORMAT.exec(format);

  if (standard === null) {
    return null;
  }

  const [, letter = "", digits = ""] = standard;
  const precision = digits === "" ? null : Number(digits);

  if (precision !== null && precision > MAX_PRECISION) {
    throw invalidFormat();
  }

  return { letter, precision };
}

/** The `FormatException` .NET throws for an invalid format. */
export function invalidFormat(): FormatException {
  return new FormatException(INVALID_FORMAT_MESSAGE);
}

/** The `NotSupportedException` for a valid .NET format the runtime does not implement. */
export function unsupportedFormat(format: string, values: string): NotSupportedException {
  return new NotSupportedException(
    `The format '${format}' is not supported for ${values} in TypeScript.`,
  );
}

/**
 * `value` in the standard format shared by every numeric type — `F`, `N`,
 * `E`, `P` and `C` — or `null` for another letter, which the caller formats
 * or rejects itself.
 */
export function formatCommonStandard(
  value: DecimalDigits,
  format: StandardFormat,
  rounding: Rounding,
): string | null {
  const { letter, precision } = format;

  switch (letter.toUpperCase()) {
    case "F":
      return signed(value, fixedPoint(value, precision ?? DEFAULT_DECIMALS, rounding, false));
    case "N":
      return signed(value, fixedPoint(value, precision ?? DEFAULT_DECIMALS, rounding, true));
    case "E":
      return signed(
        value,
        scientific(value, precision ?? DEFAULT_EXPONENT_DECIMALS, rounding, letter),
      );
    case "P":
      return signed(value, `${percentDigits(value, precision, rounding)} %`);
    case "C":
      return currency(value, fixedPoint(value, precision ?? DEFAULT_DECIMALS, rounding, true));
  }

  return null;
}

/**
 * `G[n]` with a precision: `value` rounded to `precision` significant digits,
 * in fixed-point notation unless its exponent is at least `precision` or below
 * -5 (`exponentLetter` is the `G`'s case as `E` / `e`).
 */
export function generalWithPrecision(
  value: DecimalDigits,
  precision: number,
  rounding: Rounding,
  exponentLetter: string,
): string {
  return generalNotation(roundedAt(value, precision, rounding), precision, exponentLetter);
}

/**
 * `G` notation of digits already rounded: fixed-point unless the scale
 * exceeds `maxDigits` or the value is below `0.0001`, trailing zeros dropped,
 * a two-digit minimum exponent (`1.2346E+06`).
 */
export function generalNotation(
  value: DecimalDigits,
  maxDigits: number,
  exponentLetter: string,
): string {
  const { digits, scale } = value;

  if (digits === "") {
    return signed(value, "0");
  }

  const isScientific = scale > maxDigits || scale < MIN_GENERAL_FIXED_SCALE;

  if (isScientific) {
    const mantissa = digits.length > 1 ? `${digits[0]}.${digits.slice(1)}` : digits;

    return signed(
      value,
      mantissa + exponentText(exponentLetter, scale - 1, MIN_GENERAL_EXPONENT_DIGITS),
    );
  }

  return signed(value, plainNotation(digits, scale));
}

/** The digits `value` keeps when only its first `keep` digits survive, rounded by `rounding`. */
export function roundedAt(value: DecimalDigits, keep: number, rounding: Rounding): DecimalDigits {
  const { digits } = value;

  if (keep >= digits.length) {
    return value;
  }

  if (keep < 0) {
    return decimalDigits(value.negative, "", 0);
  }

  const kept = digits.slice(0, keep);

  return roundsUp(digits, keep, rounding)
    ? incremented(value, kept)
    : decimalDigits(value.negative, kept, value.scale);
}

/**
 * The value `±0.digits × 10^scale`, trailing zeros dropped; zero has no
 * digits and scale 0, and keeps `negative`.
 */
export function decimalDigits(negative: boolean, digits: string, scale: number): DecimalDigits {
  const significant = digits.replace(/0+$/, "");

  return significant === ""
    ? { negative, digits: "", scale: 0 }
    : { negative, digits: significant, scale };
}

/** `text` with the minus sign of a negative `value` (a negative zero included). */
export function signed(value: DecimalDigits, text: string): string {
  return value.negative ? `-${text}` : text;
}

/** `digits` in plain notation: `123.45`, `0.00123`, `12300`. */
function plainNotation(digits: string, scale: number): string {
  if (scale <= 0) {
    return `0.${"0".repeat(-scale)}${digits}`;
  }

  if (scale >= digits.length) {
    return digits.padEnd(scale, "0");
  }

  return `${digits.slice(0, scale)}.${digits.slice(scale)}`;
}

/** `value`'s magnitude with `decimals` fraction digits, the integer digits grouped by thousands when asked. */
function fixedPoint(
  value: DecimalDigits,
  decimals: number,
  rounding: Rounding,
  grouping: boolean,
): string {
  const rounded = roundedAt(value, value.scale + decimals, rounding);
  const integer = integerDigits(rounded);
  const head = grouping ? grouped(integer) : integer;

  return decimals === 0 ? head : `${head}.${fractionDigits(rounded, decimals)}`;
}

function integerDigits(value: DecimalDigits): string {
  return value.scale <= 0 ? "0" : value.digits.slice(0, value.scale).padEnd(value.scale, "0");
}

/** The first `decimals` digits after the decimal point. */
function fractionDigits(value: DecimalDigits, decimals: number): string {
  const leadingZeros = Math.min(Math.max(-value.scale, 0), decimals);
  const start = Math.max(value.scale, 0);
  const taken = value.digits.slice(start, start + decimals - leadingZeros);

  return ("0".repeat(leadingZeros) + taken).padEnd(decimals, "0");
}

function grouped(digits: string): string {
  const head = digits.length % GROUP_SIZE || GROUP_SIZE;
  const groups = [digits.slice(0, head)];

  for (let start = head; start < digits.length; start += GROUP_SIZE) {
    groups.push(digits.slice(start, start + GROUP_SIZE));
  }

  return groups.join(",");
}

/** `E[n]`: one integer digit, `decimals` fraction digits and a three-digit minimum exponent. */
function scientific(
  value: DecimalDigits,
  decimals: number,
  rounding: Rounding,
  letter: string,
): string {
  const rounded = roundedAt(value, decimals + 1, rounding);
  const exponent = rounded.digits === "" ? 0 : rounded.scale - 1;
  const first = rounded.digits[0] ?? "0";

  const mantissa =
    decimals === 0 ? first : `${first}.${rounded.digits.slice(1).padEnd(decimals, "0")}`;

  return mantissa + exponentText(letter, exponent, MIN_SCIENTIFIC_EXPONENT_DIGITS);
}

/** `P[n]`: the magnitude times 100 (its digits two places on), grouped, before the ` %`. */
function percentDigits(value: DecimalDigits, precision: number | null, rounding: Rounding): string {
  const hundredfold = decimalDigits(
    value.negative,
    value.digits,
    value.scale + PERCENT_SCALE_SHIFT,
  );

  return fixedPoint(hundredfold, precision ?? DEFAULT_DECIMALS, rounding, true);
}

/** `C[n]` in the invariant culture: `¤1,234.50`, a negative value in parentheses. */
function currency(value: DecimalDigits, magnitude: string): string {
  return value.negative ? `(${CURRENCY_SYMBOL}${magnitude})` : `${CURRENCY_SYMBOL}${magnitude}`;
}

function exponentText(letter: string, exponent: number, minDigits: number): string {
  const sign = exponent < 0 ? "-" : "+";

  return `${letter}${sign}${String(Math.abs(exponent)).padStart(minDigits, "0")}`;
}

/**
 * Whether dropping the digits from `keep` on rounds the kept ones up. The
 * digits have no trailing zeros, so a `5` followed by anything is past the
 * midpoint, and a lone `5` is the midpoint itself.
 */
function roundsUp(digits: string, keep: number, rounding: Rounding): boolean {
  const dropped = digits.charAt(keep);

  if (dropped !== MIDPOINT_DIGIT) {
    return dropped > MIDPOINT_DIGIT;
  }

  const isPastMidpoint = keep + 1 < digits.length;

  if (rounding === "halfAwayFromZero" || isPastMidpoint) {
    return true;
  }

  const lastKept = keep === 0 ? 0 : Number(digits.charAt(keep - 1));

  return lastKept % 2 === 1;
}

/** `kept` plus one unit in its last place; a carry out of the first digit raises the scale. */
function incremented(value: DecimalDigits, kept: string): DecimalDigits {
  const next = (BigInt(kept === "" ? "0" : kept) + 1n).toString();

  return decimalDigits(value.negative, next, value.scale + next.length - kept.length);
}
