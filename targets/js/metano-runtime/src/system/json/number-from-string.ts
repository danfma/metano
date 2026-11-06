import type { IntegerTypeName, NumberTypeName } from "./types.ts";

/**
 * System.Text.Json's grammar for numbers read from JSON strings
 * (`JsonNumberHandling.AllowReadingFromString`, FR-048), measured on .NET 10:
 *
 * - signed integers (`SByte`, `Int16`, `Int32`, `Int64`): an optional `+` or
 *   `-`, then ASCII digits, leading zeros allowed, within the type's range;
 * - unsigned integers (`Byte`, `UInt16`, `UInt32`, `UInt64`): ASCII digits
 *   only, so `"+5"` and `"-0"` are rejected;
 * - the 64-bit integers (`Int64`, `UInt64`) read exactly (spec 012 FR-054):
 *   `readNumberText` answers a `bigint` within the type's range, where the
 *   other integers answer a `number`;
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

/**
 * A reader whose value may be a `bigint`: the 64-bit integers read exactly
 * (spec 012 FR-054), the rest read a `number`.
 */
type WideNumberTextReader = (text: string) => number | bigint | undefined;

/** Whether the matched text, parsed as `value`, stays finite in the target type. */
type FiniteCheck = (parts: RegExpExecArray, value: number) => boolean;

/**
 * The JSON integer token a number leaf holds (`JSON.rawJSON`'s text, the
 * exact-digit leaf `serialize` writes): `-?(0|[1-9][0-9]*)` — no sign, no
 * leading zero and no fraction or exponent, so `"-0"` reads `0` and `1.0`,
 * `1e3` and `05` are not tokens — within `numberType`'s range, per its
 * signedness (spec 012 FR-052). Answers a `bigint`, or `undefined` when the
 * text is not a token of the type.
 */
const SIGNED_INTEGER_TOKEN = /^-?(0|[1-9][0-9]*)$/;

const UNSIGNED_INTEGER_TOKEN = /^(0|[1-9][0-9]*)$/;

export function readIntegerToken(text: string, numberType: IntegerTypeName): bigint | undefined {
  const token = UNSIGNED_INTEGER_TYPES.has(numberType)
    ? UNSIGNED_INTEGER_TOKEN
    : SIGNED_INTEGER_TOKEN;

  if (!token.test(text)) {
    return undefined;
  }

  const [min, max] = INTEGER_RANGES[numberType];
  const value = BigInt(text);

  return isWithinRange(value, min, max) ? value : undefined;
}

/**
 * Reads the integer grammar within `[min, max]`; `ofValue` converts the
 * exact `bigint` into the value the reader answers (the `bigint` itself for
 * the 64-bit types, a `number` elsewhere).
 */
function integerReader<T extends number | bigint>(
  pattern: RegExp,
  min: bigint,
  max: bigint,
  ofValue: (value: bigint) => T,
): (text: string) => T | undefined {
  return (text) => {
    if (!pattern.test(text)) {
      return undefined;
    }

    const value = BigInt(text);

    // The bigint has no negative zero, so `"-0"` reads as `0`.
    return isWithinRange(value, min, max) ? ofValue(value) : undefined;
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

/** The range of each integer type. */
const INTEGER_RANGES: Readonly<Record<IntegerTypeName, readonly [min: bigint, max: bigint]>> = {
  "System.Byte": [0n, 255n],
  "System.SByte": [-128n, 127n],
  "System.Int16": [-32_768n, 32_767n],
  "System.UInt16": [0n, 65_535n],
  "System.Int32": [-2_147_483_648n, 2_147_483_647n],
  "System.UInt32": [0n, 4_294_967_295n],
  "System.Int64": [-9_223_372_036_854_775_808n, 9_223_372_036_854_775_807n],
  "System.UInt64": [0n, 18_446_744_073_709_551_615n],
};

/** The `[min, max]` of an integer type's range, shared with the `int64` converter. */
export function integerRangeOf(numberType: IntegerTypeName): readonly [min: bigint, max: bigint] {
  return INTEGER_RANGES[numberType];
}

/** The unsigned integer types, read from digits only. */
const UNSIGNED_INTEGER_TYPES: ReadonlySet<IntegerTypeName> = new Set([
  "System.Byte",
  "System.UInt16",
  "System.UInt32",
  "System.UInt64",
]);

/** The integers that read exactly, as the `bigint` they compute (spec 012 FR-054). */
const EXACT_INTEGER_TYPES: ReadonlySet<IntegerTypeName> = new Set([
  "System.Int64",
  "System.UInt64",
]);

/** The value of a strict read: the `bigint` for a 64-bit type, its `number` elsewhere. */
function strictValueOf(numberType: IntegerTypeName): (value: bigint) => number | bigint {
  return EXACT_INTEGER_TYPES.has(numberType) ? (value) => value : (value) => Number(value);
}

/**
 * A reader of the integers in `numberType`'s range, spelled as `pattern`
 * allows.
 */
function integerTextReader<T extends number | bigint>(
  numberType: IntegerTypeName,
  pattern: RegExp,
  ofValue: (value: bigint) => T,
): (text: string) => T | undefined {
  const [min, max] = INTEGER_RANGES[numberType];

  return integerReader(pattern, min, max, ofValue);
}

/** Every integer type read by `patternOf` its type and `ofValue` its value. */
function integerReadersOf<T extends number | bigint>(
  patternOf: (numberType: IntegerTypeName) => RegExp,
  ofValue: (numberType: IntegerTypeName) => (value: bigint) => T,
): Readonly<Record<IntegerTypeName, (text: string) => T | undefined>> {
  const numberTypes = Object.keys(INTEGER_RANGES) as IntegerTypeName[];

  return Object.fromEntries(
    numberTypes.map((numberType) => [
      numberType,
      integerTextReader(numberType, patternOf(numberType), ofValue(numberType)),
    ]),
  ) as Readonly<Record<IntegerTypeName, (text: string) => T | undefined>>;
}

/** Signed integer types take an optional sign; unsigned ones digits only. */
const STRICT_INTEGER_READERS = integerReadersOf(
  (numberType) =>
    UNSIGNED_INTEGER_TYPES.has(numberType) ? UNSIGNED_INTEGER_PATTERN : SIGNED_INTEGER_PATTERN,
  strictValueOf,
);

/** Every integer type with an optional sign, as `Enum.TryParse` reads one (`readEnumIntegerText`). */
const ENUM_INTEGER_READERS = integerReadersOf(
  () => SIGNED_INTEGER_PATTERN,
  () => (value) => Number(value),
);

const NUMBER_TEXT_READERS: Readonly<Record<NumberTypeName, WideNumberTextReader>> = {
  ...STRICT_INTEGER_READERS,
  "System.Single": floatingPointReader(fitsSingle),
  "System.Double": floatingPointReader(fitsDouble),
};

/**
 * The number `text` spells as a `numberType`, or `undefined` when System.Text.Json
 * rejects it. The 64-bit types answer a `bigint` in range (spec 012 FR-054);
 * the rest answer a `number`.
 */
export function readNumberText(
  text: string,
  numberType: NumberTypeName,
): number | bigint | undefined {
  return NUMBER_TEXT_READERS[numberType](text);
}

/**
 * The number `text` spells as a `numberType`, as the `number` a `primitive`
 * descriptor's slot holds: the 64-bit answer of `readNumberText` is read as
 * a `number` there (spec 012 FR-054), which is exact only up to 2^53 — the
 * exact digits of a `long` / `ulong` need the `int64` descriptor.
 */
export function readNumberValue(text: string, numberType: NumberTypeName): number | undefined {
  const value = readNumberText(text, numberType);

  return typeof value === "bigint" ? Number(value) : value;
}

/**
 * The integer an enum key `text` spells within the range of its underlying
 * `numberType`, with an optional sign whatever the type, as `Enum.TryParse`
 * reads it (`"+5"` and `"-0"` read for a `byte` enum); `undefined` otherwise.
 */
export function readEnumIntegerText(text: string, numberType: IntegerTypeName): number | undefined {
  return ENUM_INTEGER_READERS[numberType](text) as number | undefined;
}

/** The digits of a matched number text as `significand × 10^exponent`, without its sign. */
export function significandPartsOf(parts: RegExpExecArray): SignificandParts {
  const { integer = "", fraction = "", bareFraction = "", exponent = "0" } = parts.groups ?? {};
  const fractionDigits = fraction + bareFraction;
  const integerIsZero = leadingZerosOf(integer) === integer.length;

  return {
    significand: (integer + fractionDigits).replace(LEADING_ZEROS_PATTERN, ""),
    exponent: Number(exponent) - fractionDigits.length,
    leadingFractionZeros: integerIsZero ? leadingZerosOf(fractionDigits) : 0,
  };
}

function leadingZerosOf(digits: string): number {
  return LEADING_ZEROS_PATTERN.exec(digits)?.[0].length ?? 0;
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
