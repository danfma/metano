import type { NumberTypeName } from "./types.ts";

/**
 * System.Text.Json's grammar for numbers read from JSON strings
 * (`JsonNumberHandling.AllowReadingFromString`, FR-048), measured on .NET 10:
 *
 * - signed integers (`SByte`, `Int16`, `Int32`, `Int64`): an optional `+` or
 *   `-`, then ASCII digits, leading zeros allowed, within the type's range;
 * - unsigned integers (`Byte`, `UInt16`, `UInt32`, `UInt64`): ASCII digits
 *   only, so `"+5"` and `"-0"` are rejected;
 * - `Single` / `Double`: the exact texts `NaN`, `Infinity` and `-Infinity`,
 *   or an optional sign, digits with an optional `.` (`"1."`, `".5"` and
 *   `"01.5"` included) and an optional exponent; a value that overflows the
 *   type is rejected, one that underflows reads as zero;
 * - `Decimal`: the same text without the named literals, within ±79228162514264337593543950335
 *   after rounding.
 *
 * No whitespace, hex, digit separator, empty string or other spelling is
 * accepted.
 */

/** Signed integer text: an optional sign, then ASCII digits. */
const SIGNED_INTEGER_PATTERN = /^[+-]?[0-9]+$/;

/** Unsigned integer text: ASCII digits only, without any sign. */
const UNSIGNED_INTEGER_PATTERN = /^[0-9]+$/;

/** Floating-point and decimal text: sign, integer digits, fraction digits, exponent. */
const REAL_NUMBER_PATTERN = /^[+-]?(?:([0-9]+)(?:\.([0-9]*))?|\.([0-9]+))(?:[eE]([+-]?[0-9]+))?$/;

/** The named floating-point literals, matched case-sensitively. */
const FLOATING_POINT_LITERALS: ReadonlyMap<string, number> = new Map([
  ["NaN", Number.NaN],
  ["Infinity", Number.POSITIVE_INFINITY],
  ["-Infinity", Number.NEGATIVE_INFINITY],
]);

/** The number of integer digits of `System.Decimal.MaxValue`. */
const DECIMAL_INTEGER_DIGITS = 29;

/**
 * `2 × 79228162514264337593543950335 + 1`: a decimal text is out of range
 * when twice its magnitude reaches it, as it then rounds past the maximum.
 */
const DECIMAL_DOUBLED_OVERFLOW = 158_456_325_028_528_675_187_087_900_671n;

/** Reads a JSON string as a number, or `undefined` when the grammar rejects it. */
type NumberTextReader = (text: string) => number | undefined;

const NUMBER_TEXT_READERS: Readonly<Record<NumberTypeName, NumberTextReader>> = {
  "System.Byte": integerReader(UNSIGNED_INTEGER_PATTERN, 0n, 255n),
  "System.SByte": integerReader(SIGNED_INTEGER_PATTERN, -128n, 127n),
  "System.Int16": integerReader(SIGNED_INTEGER_PATTERN, -32_768n, 32_767n),
  "System.UInt16": integerReader(UNSIGNED_INTEGER_PATTERN, 0n, 65_535n),
  "System.Int32": integerReader(SIGNED_INTEGER_PATTERN, -2_147_483_648n, 2_147_483_647n),
  "System.UInt32": integerReader(UNSIGNED_INTEGER_PATTERN, 0n, 4_294_967_295n),
  "System.Int64": integerReader(
    SIGNED_INTEGER_PATTERN,
    -9_223_372_036_854_775_808n,
    9_223_372_036_854_775_807n,
  ),
  "System.UInt64": integerReader(UNSIGNED_INTEGER_PATTERN, 0n, 18_446_744_073_709_551_615n),
  "System.Single": floatingPointReader(Math.fround),
  "System.Double": floatingPointReader((value) => value),
};

/**
 * The number `text` spells as a `numberType`, or `undefined` when System.Text.Json
 * rejects it.
 */
export function readNumberText(text: string, numberType: NumberTypeName): number | undefined {
  return NUMBER_TEXT_READERS[numberType](text);
}

/** Whether System.Text.Json reads `text` as a `System.Decimal`. */
export function isDecimalText(text: string): boolean {
  const parts = REAL_NUMBER_PATTERN.exec(text);

  return parts !== null && fitsDecimal(parts);
}

function integerReader(pattern: RegExp, min: bigint, max: bigint): NumberTextReader {
  return (text) => {
    if (!pattern.test(text)) {
      return undefined;
    }

    const value = BigInt(text);

    // `+ 0` turns the `-0` of `"-0"` into `0`, as an integer has no negative zero.
    return value < min || value > max ? undefined : Number(text) + 0;
  };
}

/**
 * Reads the floating-point grammar; `toTargetPrecision` rounds the value to
 * the target type only to detect an overflow. The value keeps double
 * precision, as a JSON number read into a `Single` does.
 */
function floatingPointReader(toTargetPrecision: (value: number) => number): NumberTextReader {
  return (text) => {
    const literal = FLOATING_POINT_LITERALS.get(text);

    if (literal !== undefined) {
      return literal;
    }

    if (!REAL_NUMBER_PATTERN.test(text)) {
      return undefined;
    }

    const value = Number(text);

    return Number.isFinite(toTargetPrecision(value)) ? value : undefined;
  };
}

/**
 * Whether the matched decimal text fits `System.Decimal` once rounded: its
 * significant digits times a power of ten stay below half past the maximum.
 */
function fitsDecimal(parts: RegExpExecArray): boolean {
  const [, integerDigits = "", fractionDigits = "", bareFraction = "", exponentText = "0"] = parts;
  const fraction = fractionDigits + bareFraction;
  const significand = (integerDigits + fraction).replace(/^0+/, "");

  if (significand === "") {
    return true;
  }

  const exponent = Number(exponentText) - fraction.length;
  const integerDigitCount = significand.length + exponent;

  if (integerDigitCount !== DECIMAL_INTEGER_DIGITS) {
    return integerDigitCount < DECIMAL_INTEGER_DIGITS;
  }

  const doubled = 2n * BigInt(significand);

  return exponent >= 0
    ? doubled * 10n ** BigInt(exponent) < DECIMAL_DOUBLED_OVERFLOW
    : doubled < DECIMAL_DOUBLED_OVERFLOW * 10n ** BigInt(-exponent);
}
