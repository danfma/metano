/**
 * `ToString(format)` and the interpolation holes `{value,alignment:format}` of
 * C#'s 64-bit integers in the invariant culture. `bigint` has no formatting of
 * its own beyond `toString(radix)`, and `Intl.NumberFormat` rounds through
 * locale data, so the standard formats .NET applies to integers are written
 * out here from the digits.
 */
import { FormatException, NotSupportedException } from "./exceptions/index.ts";

/** .NET's message for a format string that is neither standard nor custom. */
const INVALID_FORMAT_MESSAGE = "Format specifier was invalid.";

/** A standard numeric format: one letter and an optional precision. */
const STANDARD_FORMAT = /^([A-Za-z])(\d*)$/;

/** The largest precision .NET accepts in a standard format. */
const MAX_PRECISION = 999_999_999;

/** The standard formats .NET defines for integers that {@link formatInt64} does not implement. */
const UNSUPPORTED_STANDARD_FORMATS = new Set(["B", "C", "E", "P"]);

/** The digits of a thousands group. */
const GROUP_SIZE = 3;

/** `N` and `F` print two decimals when the format gives no precision. */
const DEFAULT_DECIMALS = 2;

/**
 * `value.ToString(format)` for a `long` or `ulong`, padded to `alignment` as
 * an interpolation hole is (left when the alignment is negative). `format`
 * `null`, `undefined` or `""` is `G`. Implements `G[n]`, `R`, `D[n]`,
 * `N[n]`, `F[n]` and `X[n]` / `x[n]`; hex prints a negative value as its
 * 64-bit two's complement.
 *
 * @throws NotSupportedException for a valid .NET format outside that set
 *   (`C`, `P`, `E`, `B` or a custom format such as `#,##0`).
 * @throws FormatException for an invalid format (`Q`).
 */
export function formatInt64(value: bigint, format?: string | null, alignment?: number): string {
  const text = formatValue(value, format ?? "");

  return alignment === undefined ? text : aligned(text, alignment);
}

function formatValue(value: bigint, format: string): string {
  if (format === "") {
    return value.toString();
  }

  const standard = STANDARD_FORMAT.exec(format);

  if (standard === null) {
    throw unsupported(format);
  }

  const [, letter = "", digits = ""] = standard;
  const precision = digits === "" ? null : Number(digits);

  if (precision !== null && precision > MAX_PRECISION) {
    throw new FormatException(INVALID_FORMAT_MESSAGE);
  }

  return formatStandard(value, letter, precision, format);
}

function formatStandard(
  value: bigint,
  letter: string,
  precision: number | null,
  format: string,
): string {
  const sign = value < 0n ? "-" : "";
  const magnitude = (value < 0n ? -value : value).toString();

  switch (letter.toUpperCase()) {
    case "G":
      return sign + general(magnitude, precision, letter === "g" ? "e" : "E");
    case "R":
      return sign + magnitude;
    case "D":
      return sign + magnitude.padStart(precision ?? 0, "0");
    case "N":
      return sign + withDecimals(grouped(magnitude), precision ?? DEFAULT_DECIMALS);
    case "F":
      return sign + withDecimals(magnitude, precision ?? DEFAULT_DECIMALS);
    case "X":
      return hexadecimal(value, precision ?? 0, letter === "x");
  }

  throw UNSUPPORTED_STANDARD_FORMATS.has(letter.toUpperCase())
    ? unsupported(format)
    : new FormatException(INVALID_FORMAT_MESSAGE);
}

/**
 * `G[n]`: the digits when they fit the precision, otherwise scientific
 * notation rounded half away from zero to `precision` significant digits,
 * trailing zeros dropped and a two-digit exponent (`1.2346E+06`).
 */
function general(magnitude: string, precision: number | null, exponentLetter: string): string {
  if (precision === null || precision === 0 || precision >= magnitude.length) {
    return magnitude;
  }

  const rounded = roundedSignificantDigits(magnitude, precision);
  const exponent = magnitude.length - 1 + (rounded.length > precision ? 1 : 0);
  const significand = rounded.slice(0, precision).replace(/0+$/, "");
  const mantissa =
    significand.length > 1 ? `${significand[0]}.${significand.slice(1)}` : significand;

  return `${mantissa}${exponentLetter}+${String(exponent).padStart(2, "0")}`;
}

/** The first `precision` digits of `magnitude`, rounded half away from zero (one digit longer on a carry). */
function roundedSignificantDigits(magnitude: string, precision: number): string {
  const kept = BigInt(magnitude.slice(0, precision));
  const roundsUp = Number(magnitude[precision]) >= 5;

  return (roundsUp ? kept + 1n : kept).toString();
}

function grouped(digits: string): string {
  const head = digits.length % GROUP_SIZE || GROUP_SIZE;
  const groups = [digits.slice(0, head)];

  for (let start = head; start < digits.length; start += GROUP_SIZE) {
    groups.push(digits.slice(start, start + GROUP_SIZE));
  }

  return groups.join(",");
}

function withDecimals(integer: string, decimals: number): string {
  return decimals === 0 ? integer : `${integer}.${"0".repeat(decimals)}`;
}

function hexadecimal(value: bigint, minimumDigits: number, lowercase: boolean): string {
  const digits = BigInt.asUintN(64, value).toString(16).padStart(minimumDigits, "0");

  return lowercase ? digits : digits.toUpperCase();
}

function aligned(text: string, alignment: number): string {
  return alignment < 0 ? text.padEnd(-alignment) : text.padStart(alignment);
}

function unsupported(format: string): NotSupportedException {
  return new NotSupportedException(
    `The format '${format}' is not supported for 64-bit integers in TypeScript.`,
  );
}
