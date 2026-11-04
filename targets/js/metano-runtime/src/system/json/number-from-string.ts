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
 * - `Decimal`: the same text without the named literals, within
 *   ±79228162514264337593543950335 after rounding (`decimal-text.ts`).
 *
 * No whitespace, hex, digit separator, empty string or other spelling is
 * accepted.
 */

/** Signed integer text: an optional sign, then ASCII digits. */
const SIGNED_INTEGER_PATTERN = /^[+-]?[0-9]+$/;

/** Unsigned integer text: ASCII digits only, without any sign. */
const UNSIGNED_INTEGER_PATTERN = /^[0-9]+$/;

/** Floating-point and decimal text: sign, integer digits, fraction digits, exponent. */
export const REAL_NUMBER_PATTERN =
  /^[+-]?(?:(?<integer>[0-9]+)(?:\.(?<fraction>[0-9]*))?|\.(?<bareFraction>[0-9]+))(?:[eE](?<exponent>[+-]?[0-9]+))?$/;

const LEADING_ZEROS_PATTERN = /^0+/;

/** The named floating-point literals, matched case-sensitively. */
const FLOATING_POINT_LITERALS: ReadonlyMap<string, number> = new Map([
  ["NaN", Number.NaN],
  ["Infinity", Number.POSITIVE_INFINITY],
  ["-Infinity", Number.NEGATIVE_INFINITY],
]);

/** A positive bound written as `limit × 10^-scale`. */
interface Bound {
  readonly limit: bigint;
  readonly scale: number;
}

/**
 * `2^128 − 2^103`, half-way between `Single.MaxValue` and `2^128`: a `Single`
 * text at or past it rounds to infinity (measured: `"3.4028235677973366e38"`
 * reads, the exact midpoint throws). It is a double, so a parsed double
 * equal to it is the only case the text itself must decide.
 */
const SINGLE_OVERFLOW: Bound = { limit: 2n ** 128n - 2n ** 103n, scale: 0 };

const SINGLE_OVERFLOW_AS_DOUBLE = Number(SINGLE_OVERFLOW.limit);

/** A matched number text as `significand × 10^exponent`, without its sign. */
export interface SignificandParts {
  /** The significant digits without leading zeros; empty for zero. */
  readonly significand: string;
  readonly exponent: number;
  /**
   * The zeros that open the fraction when the integer part is zero (`0.001`
   * has 2); 0 otherwise. System.Decimal rounds a tie by a position that
   * counts them (`decimal-text.ts`).
   */
  readonly leadingFractionZeros: number;
}

/** Reads a JSON string as a number, or `undefined` when the grammar rejects it. */
type NumberTextReader = (text: string) => number | undefined;

/** Whether the matched text, parsed as `value`, stays finite in the target type. */
type FiniteCheck = (parts: RegExpExecArray, value: number) => boolean;

/**
 * Reads the integer grammar within `[min, max]`. The value is a JS `number`,
 * so an `Int64` / `UInt64` beyond 2^53 loses precision exactly as the same
 * JSON number does through `JSON.parse`.
 */
function integerReader(pattern: RegExp, min: bigint, max: bigint): NumberTextReader {
  return (text) => {
    if (!pattern.test(text)) {
      return undefined;
    }

    const value = BigInt(text);

    // The bigint has no negative zero, so `"-0"` reads as `0`.
    return isWithinRange(value, min, max) ? Number(value) : undefined;
  };
}

function isWithinRange(value: bigint, min: bigint, max: bigint): boolean {
  return value >= min && value <= max;
}

/**
 * Reads the floating-point grammar; `staysFinite` rejects a value that
 * overflows the target type. The value keeps double precision, as a JSON
 * number read into a `Single` does.
 */
function floatingPointReader(staysFinite: FiniteCheck): NumberTextReader {
  return (text) => {
    const literal = FLOATING_POINT_LITERALS.get(text);

    if (literal !== undefined) {
      return literal;
    }

    const parts = REAL_NUMBER_PATTERN.exec(text);

    if (parts === null) {
      return undefined;
    }

    const value = Number(text);

    return staysFinite(parts, value) ? value : undefined;
  };
}

function fitsDouble(_: RegExpExecArray, value: number): boolean {
  return Number.isFinite(value);
}

/**
 * Whether the text stays below the `Single` overflow threshold. The double
 * is already rounded, so it decides every case but one: a double equal to
 * the threshold, where the exact text decides.
 */
function fitsSingle(parts: RegExpExecArray, value: number): boolean {
  const magnitude = Math.abs(value);

  if (magnitude !== SINGLE_OVERFLOW_AS_DOUBLE) {
    return magnitude < SINGLE_OVERFLOW_AS_DOUBLE;
  }

  return isBelow(significandPartsOf(parts), SINGLE_OVERFLOW);
}

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
  "System.Single": floatingPointReader(fitsSingle),
  "System.Double": floatingPointReader(fitsDouble),
};

/**
 * The number `text` spells as a `numberType`, or `undefined` when System.Text.Json
 * rejects it.
 */
export function readNumberText(text: string, numberType: NumberTypeName): number | undefined {
  return NUMBER_TEXT_READERS[numberType](text);
}

/** The digits of a matched number text as `significand × 10^exponent`, without its sign. */
export function significandPartsOf(parts: RegExpExecArray): SignificandParts {
  const { integer = "", fraction = "", bareFraction = "", exponent = "0" } = parts.groups ?? {};
  const fractionDigits = fraction + bareFraction;
  const integerIsZero = integer.replace(LEADING_ZEROS_PATTERN, "") === "";
  const significantFraction = fractionDigits.replace(LEADING_ZEROS_PATTERN, "");

  return {
    significand: (integer + fractionDigits).replace(LEADING_ZEROS_PATTERN, ""),
    exponent: Number(exponent) - fractionDigits.length,
    leadingFractionZeros: integerIsZero ? fractionDigits.length - significantFraction.length : 0,
  };
}

/** Whether `value` is strictly below `bound`. */
function isBelow(value: SignificandParts, bound: Bound): boolean {
  return compareMagnitude(value, bound) < 0;
}

/**
 * The sign of `value − bound`, computed exactly: the digit counts decide
 * first, so a huge exponent never builds a huge power of ten.
 */
function compareMagnitude(value: SignificandParts, bound: Bound): number {
  if (value.significand === "") {
    return -1;
  }

  const shift = value.exponent + bound.scale;
  const digitDifference = value.significand.length + shift - bound.limit.toString().length;

  if (digitDifference !== 0) {
    return Math.sign(digitDifference);
  }

  return compareShifted(BigInt(value.significand), shift, bound.limit);
}

/** The sign of `significand × 10^shift − limit`. */
function compareShifted(significand: bigint, shift: number, limit: bigint): number {
  const [left, right] =
    shift >= 0
      ? [significand * 10n ** BigInt(shift), limit]
      : [significand, limit * 10n ** BigInt(-shift)];

  return left === right ? 0 : left < right ? -1 : 1;
}
