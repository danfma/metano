/**
 * `ToString(format)` and the interpolation holes `{value,alignment:format}` of
 * C#'s integers in the invariant culture: the 64-bit ones, which are `bigint`
 * values, and the 32-bit and narrower ones, which are `number` values. Both
 * families share one implementation over the value's exact digits, rounding
 * half away from zero as .NET does for integers; `Intl.NumberFormat` rounds
 * through locale data, so the formats are written out here.
 */
import {
  aligned,
  type DecimalDigits,
  formatCommonStandard,
  generalWithPrecision,
  invalidFormat,
  parseStandardFormat,
  type StandardFormat,
  unsupportedFormat,
  withDigits,
} from "./standard-numeric-format.ts";

/** The bit widths of the integers held in a `number`: `sbyte` / `byte`, `short` / `ushort`, `int` / `uint`. */
export type IntegerBits = 8 | 16 | 32;

/** The bit width of `long` and `ulong`, whose hex digits are their 64-bit two's complement. */
const INT64_BITS = 64;

/** Integers round half away from zero: a dropped digit of 5 or more carries. */
const INTEGER_ROUNDING = "halfAwayFromZero";

/** How the exceptions name the values {@link formatInt64} formats. */
const INT64_VALUES = "64-bit integers";

/** How the exceptions name the values {@link formatInteger} formats. */
const INTEGER_VALUES = "integers";

/**
 * `value.ToString(format)` for a `long` or `ulong`, padded to `alignment` as
 * an interpolation hole is (left when the alignment is negative). `format`
 * `null`, `undefined` or `""` is `G`; a `null` value (a C# `long?`) formats
 * as the empty string. Implements `G[n]`, `R[n]` (`G` for an integer), `D[n]`, `N[n]`, `F[n]`,
 * `E[n]`, `P[n]`, `C[n]` and `X[n]` / `x[n]`; hex prints a negative value as
 * its 64-bit two's complement.
 *
 * @throws NotSupportedException for a valid .NET format outside that set
 *   (`B` or a custom format such as `#,##0`).
 * @throws FormatException for an invalid format (`Q`).
 */
export function formatInt64(
  value: bigint | null | undefined,
  format?: string | null,
  alignment?: number,
): string {
  const text = value == null ? "" : formatBigInt(value, format ?? "", INT64_BITS, INT64_VALUES);

  return aligned(text, alignment);
}

/**
 * `value.ToString(format)` for an integer of `bits` bits held in a `number`
 * (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`), padded to `alignment`
 * as an interpolation hole is. The formats are {@link formatInt64}'s; hex
 * prints a negative value as its two's complement on `bits` bits.
 *
 * @throws NotSupportedException for a valid .NET format the runtime does not implement.
 * @throws FormatException for an invalid format.
 */
export function formatInteger(
  value: number | null | undefined,
  bits: IntegerBits,
  format?: string | null,
  alignment?: number,
): string {
  const text = value == null ? "" : formatBigInt(BigInt(value), format ?? "", bits, INTEGER_VALUES);

  return aligned(text, alignment);
}

function formatBigInt(value: bigint, format: string, bits: number, values: string): string {
  if (format === "") {
    return value.toString();
  }

  const standard = parseStandardFormat(format);

  if (standard === null) {
    throw unsupportedFormat(format, values);
  }

  return formatStandard(value, standard, bits) ?? rejected(format, standard, values);
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
  return withDigits({ negative: value < 0n, digits: "", scale: 0 }, magnitude, magnitude.length);
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
function rejected(format: string, standard: StandardFormat, values: string): never {
  throw standard.letter.toUpperCase() === "B" ? unsupportedFormat(format, values) : invalidFormat();
}
