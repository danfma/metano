/**
 * `ToString(format)` and the interpolation holes `{value,alignment:format}` of
 * C#'s `double` in the invariant culture, through the standard numeric
 * formats the integers share. A format with a precision rounds the exact
 * binary value half to even, as .NET Core 3.0+ does (`2.675` is
 * `2.67499999…`, so `F2` prints `2.67`; `0.125` is exact, so `F2` prints
 * `0.12`); `toFixed` would round the same exact value half up. `G` and `R`
 * without a precision print the shortest round-trip digits.
 */
import {
  type DecimalDigits,
  decimalDigits,
  formatCommonStandard,
  generalNotation,
  generalWithPrecision,
  invalidFormat,
  parseStandardFormat,
  type StandardFormat,
  unsupportedFormat,
} from "./standard-numeric-format.ts";
import { alignText } from "./strings.ts";

/** A double's exact expansion rounds half to even, as .NET's Dragon4 does. */
const DOUBLE_ROUNDING = "halfToEven";

/** How the exceptions name the values {@link formatDouble} formats. */
const DOUBLE_VALUES = "double";

/** `G` / `R` without a precision keep fixed-point notation up to 17 integer digits (`1E+17` past it). */
const ROUND_TRIP_DIGITS = 17;

/** `float`'s `G` / `R` keep fixed-point notation up to 9 integer digits (`1E+09` past it). */
const SINGLE_ROUND_TRIP_DIGITS = 9;

/** The most significant digits a `float` needs to read back as itself. */
const SINGLE_MAX_SIGNIFICANT_DIGITS = 9;

/** The bits of a double's stored fraction. */
const FRACTION_BITS = 52n;

/** The exponent of a subnormal double's fraction (`2^-1074` per unit). */
const SUBNORMAL_EXPONENT = -1074;

/** The bias of a double's stored exponent, its fraction bits included. */
const EXPONENT_BIAS = 1075;

/** `toExponential()`'s shortest round-trip digits and exponent: `1.2345e+17`. */
const EXPONENTIAL_TEXT = /^(\d)(?:\.(\d+))?e([+-]\d+)$/;

/**
 * `value.ToString(format)` for a `double`, padded to `alignment` as an
 * interpolation hole is (left when the alignment is negative). `format`
 * `null`, `undefined` or `""` is `G`; a `null` value (a C# `double?`)
 * formats as the empty string. Implements `G[n]`, `R`, `F[n]`, `N[n]`,
 * `E[n]`, `P[n]` and `C[n]`. `NaN` and the infinities print as .NET prints
 * them whatever the format, which .NET does not validate for them.
 *
 * @throws NotSupportedException for a custom format such as `0.00`.
 * @throws FormatException for a format .NET rejects for `double` (`D`, `X`).
 */
export function formatDouble(
  value: number | null | undefined,
  format?: string | null,
  alignment?: number,
): string {
  const text = value == null ? "" : formatValue(value, format ?? "");

  return alignText(text, alignment);
}

function formatValue(value: number, format: string): string {
  if (!Number.isFinite(value)) {
    return nonFiniteText(value);
  }

  const standard = format === "" ? { letter: "G", precision: null } : parseStandardFormat(format);

  if (standard === null) {
    throw unsupportedFormat(format, DOUBLE_VALUES);
  }

  const text = formatStandard(value, standard);

  if (text === null) {
    throw invalidFormat();
  }

  return text;
}

/** The text of a finite `value` in a standard format, or `null` when the letter is not a `double` format. */
function formatStandard(value: number, format: StandardFormat): string | null {
  const { letter, precision } = format;

  switch (letter.toUpperCase()) {
    case "G":
      return precision === null || precision === 0
        ? roundTrip(value, exponentOf(letter))
        : generalWithPrecision(exactDigits(value), precision, DOUBLE_ROUNDING, exponentOf(letter));
    case "R":
      return roundTrip(value, letter === "r" ? "e" : "E");
  }

  return formatCommonStandard(exactDigits(value), format, DOUBLE_ROUNDING);
}

/** `NaN`, `Infinity` or `-Infinity`. */
function nonFiniteText(value: number): string {
  if (Number.isNaN(value)) {
    return "NaN";
  }

  return value > 0 ? "Infinity" : "-Infinity";
}

/** `G`'s exponent letter: `E`, or `e` for `g`. */
function exponentOf(letter: string): string {
  return letter === "g" ? "e" : "E";
}

/** `G` / `R` without a precision: the shortest digits that read back as `value`. */
function roundTrip(value: number, exponentLetter: string): string {
  const digits = shortestDigits(value);

  return generalNotation(digits, Math.max(digits.digits.length, ROUND_TRIP_DIGITS), exponentLetter);
}

/**
 * The shortest round-trip digits of a finite `value`, read from
 * `toExponential()`, whose digits both engines compute alike; `String(value)`
 * switches notation differently in JavaScriptCore and V8.
 */
function shortestDigits(value: number): DecimalDigits {
  return exponentialDigits(isNegative(value), Math.abs(value).toExponential());
}

/** The digits and scale of the `toExponential()` text of a magnitude. */
function exponentialDigits(negative: boolean, exponential: string): DecimalDigits {
  const [, head = "", tail = "", exponent = "0"] = EXPONENTIAL_TEXT.exec(exponential) ?? [];

  return decimalDigits(negative, head + tail, Number(exponent) + 1);
}

/**
 * `float`'s default text (`$"{value}"`, `value.ToString()`, `"x" + value`): the shortest
 * digits that read back as the same single-precision value, in `G` notation. The generated
 * code holds a `float` in a JavaScript `number` that need not be a single-precision value
 * (`1f / 3` is a double there), so the value is first rounded to the `float` C# would hold.
 * A `null` value (a C# `float?`) is the empty string.
 */
export function formatSingle(value: number | null | undefined): string {
  if (value == null) {
    return "";
  }

  const single = Math.fround(value);

  if (!Number.isFinite(single)) {
    return nonFiniteText(single);
  }

  const digits = shortestSingleDigits(single);

  return generalNotation(digits, Math.max(digits.digits.length, SINGLE_ROUND_TRIP_DIGITS), "E");
}

/** The shortest digits whose value rounds to the single-precision `value`. */
function shortestSingleDigits(value: number): DecimalDigits {
  const negative = isNegative(value);
  const magnitude = Math.abs(value);

  for (let digits = 1; digits < SINGLE_MAX_SIGNIFICANT_DIGITS; digits++) {
    const candidate = magnitude.toExponential(digits - 1);

    if (Math.fround(Number(candidate)) === magnitude) {
      return exponentialDigits(negative, candidate);
    }
  }

  return exponentialDigits(negative, magnitude.toExponential(SINGLE_MAX_SIGNIFICANT_DIGITS - 1));
}

/**
 * The exact decimal expansion of a finite `value`: its binary fraction times
 * a power of two, written out with `bigint` arithmetic (`m × 2^-k` is
 * `m × 5^k / 10^k`).
 */
function exactDigits(value: number): DecimalDigits {
  const negative = isNegative(value);
  const { mantissa, exponent } = binaryParts(Math.abs(value));

  if (exponent >= 0) {
    const integer = (mantissa << BigInt(exponent)).toString();

    return decimalDigits(negative, integer, integer.length);
  }

  const scaled = (mantissa * 5n ** BigInt(-exponent)).toString();

  return decimalDigits(negative, scaled, scaled.length + exponent);
}

/** The integer `mantissa` and the `exponent` with `magnitude = mantissa × 2^exponent`. */
function binaryParts(magnitude: number): { mantissa: bigint; exponent: number } {
  const view = new DataView(new ArrayBuffer(Float64Array.BYTES_PER_ELEMENT));

  view.setFloat64(0, magnitude);

  const bits = view.getBigUint64(0);
  const storedExponent = Number(bits >> FRACTION_BITS);
  const fraction = bits & ((1n << FRACTION_BITS) - 1n);

  return storedExponent === 0
    ? { mantissa: fraction, exponent: SUBNORMAL_EXPONENT }
    : { mantissa: fraction | (1n << FRACTION_BITS), exponent: storedExponent - EXPONENT_BIAS };
}

/** Whether `value` is negative, `-0` included. */
function isNegative(value: number): boolean {
  return value < 0 || Object.is(value, -0);
}
