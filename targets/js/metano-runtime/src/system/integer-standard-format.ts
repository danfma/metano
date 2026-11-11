/**
 * The integer side of .NET's standard numeric formats in the invariant
 * culture, shared by `formatInt64` (`bigint`) and `formatInteger` (`number`):
 * the value's exact digits, rounding half away from zero as .NET does for
 * integers, `D` and `X` on top of the formats every number shares.
 * `Intl.NumberFormat` rounds through locale data, so the formats are written
 * out here. Not re-exported from the package root: the formatters are the
 * public surface.
 */
import {
  type DecimalDigits,
  decimalDigits,
  formatCommonStandard,
  generalWithPrecision,
  invalidFormat,
  parseStandardFormat,
  type StandardFormat,
  unsupportedFormat,
} from "./standard-numeric-format.ts";

/** Integers round half away from zero: a dropped digit of 5 or more carries. */
const INTEGER_ROUNDING = "halfAwayFromZero";

/**
 * `value.ToString(format)` for an integer whose hex digits are its two's
 * complement on `bits` bits; `typeName` is how the exceptions name the
 * integers (`"64-bit integers"`). `""` is `G`.
 *
 * @throws NotSupportedException for `B` or a custom format.
 * @throws FormatException for an invalid format.
 */
export function formatIntegerText(
  value: bigint,
  format: string,
  bits: number,
  typeName: string,
): string {
  if (format === "") {
    return value.toString();
  }

  const standard = parseStandardFormat(format);

  if (standard === null) {
    throw unsupportedFormat(format, typeName);
  }

  return formatStandard(value, standard, bits) ?? rejected(format, standard, typeName);
}

/** The text of `value` in a standard format, or `null` when the letter is not an integer format. */
function formatStandard(value: bigint, format: StandardFormat, bits: number): string | null {
  const sign = value < 0n ? "-" : "";
  const magnitude = (value < 0n ? -value : value).toString();
  const { letter, precision } = format;

  switch (letter.toUpperCase()) {
    case "G":
    case "R":
      return precision === null || precision === 0
        ? sign + magnitude
        : generalWithPrecision(
            digitsOf(value, magnitude),
            precision,
            INTEGER_ROUNDING,
            exponentOf(letter),
          );
    case "D":
      return sign + magnitude.padStart(precision ?? 0, "0");
    case "X":
      return hexadecimal(value, bits, precision ?? 0, letter === "x");
  }

  return formatCommonStandard(digitsOf(value, magnitude), format, INTEGER_ROUNDING);
}

/** `G`'s exponent letter: `E`, or `e` for `g` (and `r`, which is `G` for an integer). */
function exponentOf(letter: string): string {
  return letter === "g" || letter === "r" ? "e" : "E";
}

/** The exact digits of an integer whose magnitude is `magnitude`. */
function digitsOf(value: bigint, magnitude: string): DecimalDigits {
  return decimalDigits(value < 0n, magnitude, magnitude.length);
}

function hexadecimal(
  value: bigint,
  bits: number,
  minimumDigits: number,
  lowercase: boolean,
): string {
  const digits = BigInt.asUintN(bits, value).toString(16).padStart(minimumDigits, "0");

  return lowercase ? digits : digits.toUpperCase();
}

/** `B` is a valid integer format the runtime does not implement; any other letter is invalid. */
function rejected(format: string, standard: StandardFormat, typeName: string): never {
  throw standard.letter.toUpperCase() === "B"
    ? unsupportedFormat(format, typeName)
    : invalidFormat();
}
