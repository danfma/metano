/**
 * `System.Convert` into and out of C#'s 64-bit integers, which the compiler
 * maps to `bigint`. Unlike a cast, `Convert` rounds a fraction half to even,
 * always checks the range and reports .NET's per-type messages; its
 * `(string, fromBase)` overloads follow .NET's own digit grammar, which is not
 * `Parse`'s.
 */
import {
  ArgumentException,
  ArgumentOutOfRangeException,
  FormatException,
  InvalidCastException,
  OverflowException,
} from "./exceptions/index.ts";
import { INT64_MAX, INT64_MIN, UINT64_MAX } from "./int64.ts";
import { parseInt64, parseUInt64 } from "./int64-parsing.ts";

/** The .NET type a `Convert.ToXxx` call narrows a 64-bit value to. */
export type ConvertTarget =
  | "SByte"
  | "Byte"
  | "Int16"
  | "UInt16"
  | "Int32"
  | "UInt32"
  | "Int64"
  | "UInt64"
  | "Char";

/** The decimal.js surface `Convert` reads; structural, so the runtime never imports decimal.js. */
type RoundableDecimal = {
  toDecimalPlaces(decimalPlaces: number, rounding: number): { toFixed(): string };
};

/** The range and the noun of .NET's `Value was either too large or too small for …` message. */
type ConvertRange = { readonly min: bigint; readonly max: bigint; readonly noun: string };

const CONVERT_RANGES: Readonly<Record<ConvertTarget, ConvertRange>> = {
  SByte: { min: -128n, max: 127n, noun: "a signed byte" },
  Byte: { min: 0n, max: 255n, noun: "an unsigned byte" },
  Int16: { min: -32768n, max: 32767n, noun: "an Int16" },
  UInt16: { min: 0n, max: 65535n, noun: "a UInt16" },
  Int32: { min: -2147483648n, max: 2147483647n, noun: "an Int32" },
  UInt32: { min: 0n, max: 4294967295n, noun: "a UInt32" },
  Int64: { min: INT64_MIN, max: INT64_MAX, noun: "an Int64" },
  UInt64: { min: 0n, max: UINT64_MAX, noun: "a UInt64" },
  Char: { min: 0n, max: 65535n, noun: "a character" },
};

/** decimal.js's `ROUND_HALF_EVEN`. */
const DECIMAL_ROUND_HALF_EVEN = 6;

const TWO_TO_THE_63 = 2 ** 63;
const TWO_TO_THE_64 = 2 ** 64;

/** The bases `Convert.ToInt64(string, int)` accepts. */
const SUPPORTED_BASES = new Set([2, 8, 10, 16]);

/** decimal.js prints an integral value as an optional minus and digits. */
const INTEGER_TEXT = /^-?\d+$/;

/**
 * `Convert.ToInt32(long)` and the other `Convert` narrowings of a 64-bit
 * value (also `Convert.ToInt64(ulong)` and `Convert.ToUInt64(long)`):
 * returns `value` when it fits `target`.
 *
 * @throws OverflowException("Value was either too large or too small for an Int32.") and its family otherwise.
 */
export function convertNarrow(value: bigint, target: ConvertTarget): bigint {
  const range = CONVERT_RANGES[target];

  if (value < range.min || value > range.max) {
    throw rangeOverflow(range);
  }

  return value;
}

/**
 * `Convert.ToInt64(double)` / `(float)`: rounds half to even, then checks the range.
 *
 * @throws OverflowException("Arithmetic operation resulted in an overflow.") outside it, and for NaN.
 */
export function convertDoubleToInt64(value: number): bigint {
  return BigInt(checkedRoundedDouble(value, -TWO_TO_THE_63, TWO_TO_THE_63));
}

/** `Convert.ToUInt64(double)` / `(float)`: rounds half to even (`-0.5` gives `0n`), then checks the range. */
export function convertDoubleToUInt64(value: number): bigint {
  return BigInt(checkedRoundedDouble(value, 0, TWO_TO_THE_64));
}

/**
 * `Convert.ToInt64(decimal)`: rounds half to even, then checks the range.
 *
 * @throws OverflowException("Value was either too large or too small for an Int64.") outside it.
 */
export function convertDecimalToInt64(value: RoundableDecimal): bigint {
  return convertNarrow(roundedDecimal(value, CONVERT_RANGES.Int64), "Int64");
}

/** `Convert.ToUInt64(decimal)`: rounds half to even, then checks the range ("… for a UInt64."). */
export function convertDecimalToUInt64(value: RoundableDecimal): bigint {
  return convertNarrow(roundedDecimal(value, CONVERT_RANGES.UInt64), "UInt64");
}

/**
 * `Convert.ToInt64(string)` and `Convert.ToInt64(string, fromBase)`: `null`
 * is `0n`; without a base the text is parsed as `long.Parse` does; bases 2,
 * 8 and 16 read the bits as two's complement (`"FFFFFFFFFFFFFFFF"` is `-1n`).
 *
 * @throws ArgumentException for a base other than 2, 8, 10 and 16, or a minus sign in a base other than 10.
 * @throws FormatException for text with no digits of the base or with trailing characters.
 * @throws OverflowException for a value outside the range.
 */
export function convertStringToInt64(text: string | null | undefined, fromBase?: number): bigint {
  if (fromBase === undefined) {
    return text == null ? 0n : parseInt64(text);
  }

  return BigInt.asIntN(64, stringToInteger(text, fromBase, "Int64"));
}

/** `Convert.ToUInt64(string)` and `Convert.ToUInt64(string, fromBase)`; see {@link convertStringToInt64}. */
export function convertStringToUInt64(text: string | null | undefined, fromBase?: number): bigint {
  if (fromBase === undefined) {
    return text == null ? 0n : parseUInt64(text);
  }

  return stringToInteger(text, fromBase, "UInt64");
}

/**
 * `Convert.ToInt64(object)`: dispatches on the runtime value (`null`, a
 * `bigint`, a `number`, a `string`, a `boolean` or a decimal.js value). A
 * C# `char` is a one-character `string` at run time, so a boxed `char` is
 * parsed as text.
 *
 * @throws InvalidCastException for any other value.
 */
export function convertObjectToInt64(value: unknown): bigint {
  return convertObject(value, "Int64");
}

/** `Convert.ToUInt64(object)`; see {@link convertObjectToInt64}. */
export function convertObjectToUInt64(value: unknown): bigint {
  return convertObject(value, "UInt64");
}

function convertObject(value: unknown, target: "Int64" | "UInt64"): bigint {
  const unsigned = target === "UInt64";

  if (value == null) {
    return 0n;
  }

  switch (typeof value) {
    case "bigint":
      return convertNarrow(value, target);
    case "number":
      return unsigned ? convertDoubleToUInt64(value) : convertDoubleToInt64(value);
    case "string":
      return unsigned ? convertStringToUInt64(value) : convertStringToInt64(value);
    case "boolean":
      return value ? 1n : 0n;
  }

  if (isRoundableDecimal(value)) {
    return unsigned ? convertDecimalToUInt64(value) : convertDecimalToInt64(value);
  }

  throw new InvalidCastException();
}

function isRoundableDecimal(value: unknown): value is RoundableDecimal {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Partial<RoundableDecimal>).toDecimalPlaces === "function"
  );
}

function checkedRoundedDouble(value: number, min: number, maxExclusive: number): number {
  const rounded = roundHalfToEven(value);
  const fits = rounded >= min && rounded < maxExclusive;

  // NaN fails both comparisons, so it throws like ±Infinity.
  if (!fits) {
    throw new OverflowException();
  }

  // C# integers have no negative zero: `+ 0` turns the `-0` of rounding `-0.5` into `0`.
  return rounded + 0;
}

function roundHalfToEven(value: number): number {
  const floor = Math.floor(value);
  const fraction = value - floor;

  if (fraction !== 0.5) {
    return Math.round(value);
  }

  return floor % 2 === 0 ? floor : floor + 1;
}

/** `value` rounded half to even as a `bigint`; a NaN or infinite decimal.js value overflows `range`. */
function roundedDecimal(value: RoundableDecimal, range: ConvertRange): bigint {
  const text = value.toDecimalPlaces(0, DECIMAL_ROUND_HALF_EVEN).toFixed();

  if (!INTEGER_TEXT.test(text)) {
    throw rangeOverflow(range);
  }

  return BigInt(text);
}

/**
 * .NET's `ParseNumbers.StringToLong`: an optional sign (a minus only in base
 * 10), an optional `0x` in base 16, then digits of the base and nothing else;
 * no white space. Returns the unsigned 64-bit pattern of the value.
 */
function stringToInteger(
  text: string | null | undefined,
  radix: number,
  target: "Int64" | "UInt64",
): bigint {
  const unsigned = target === "UInt64";

  if (!SUPPORTED_BASES.has(radix)) {
    throw new ArgumentException("Invalid Base.");
  }

  if (text == null) {
    return 0n;
  }

  if (text === "") {
    throw new ArgumentOutOfRangeException(
      "Index was out of range. Must be non-negative and less than the size of the collection.",
    );
  }

  const negative = text[0] === "-";

  if (negative) {
    requireNegativeAllowed(radix, unsigned);
  }

  const signLength = negative || text[0] === "+" ? 1 : 0;
  const prefixLength = hexPrefixLength(text.slice(signLength), radix);
  const digits = leadingDigits(text.slice(signLength + prefixLength), radix);

  requireDigits(text, signLength + prefixLength, digits);

  return radix === 10 && !unsigned
    ? signedDecimal(digits, negative)
    : checkedUnsigned(digits, radix);
}

function requireNegativeAllowed(radix: number, unsigned: boolean): void {
  if (radix !== 10) {
    throw new ArgumentException("String cannot contain a minus sign if the base is not 10.");
  }

  if (unsigned) {
    throw new OverflowException(
      "The string was being parsed as an unsigned number and could not have a negative sign.",
    );
  }
}

function requireDigits(text: string, start: number, digits: string): void {
  if (digits === "") {
    throw new FormatException("Could not find any recognizable digits.");
  }

  if (start + digits.length < text.length) {
    throw new FormatException("Additional non-parsable characters are at the end of the string.");
  }
}

/** The length of the `0x` / `0X` prefix base 16 accepts at the start of `text`, or `0`. */
function hexPrefixLength(text: string, radix: number): number {
  return radix === 16 && /^0[xX]/.test(text) ? 2 : 0;
}

/** The longest prefix of `text` made of digits of `radix`. */
function leadingDigits(text: string, radix: number): string {
  const end = [...text].findIndex((character) => !isDigitOf(character, radix));

  return end < 0 ? text : text.slice(0, end);
}

function isDigitOf(character: string, radix: number): boolean {
  const value = Number.parseInt(character, 36);

  return !Number.isNaN(value) && value < radix;
}

function signedDecimal(digits: string, negative: boolean): bigint {
  const magnitude = BigInt(digits);
  const value = negative ? -magnitude : magnitude;

  return convertNarrow(value, "Int64");
}

/** The digits as an unsigned 64-bit value; anything wider overflows with the `UInt64` message, as .NET reports it. */
function checkedUnsigned(digits: string, radix: number): bigint {
  const value = [...digits].reduce(
    (total, digit) => total * BigInt(radix) + BigInt(Number.parseInt(digit, radix)),
    0n,
  );

  return convertNarrow(value, "UInt64");
}

function rangeOverflow(range: ConvertRange): OverflowException {
  return new OverflowException(`Value was either too large or too small for ${range.noun}.`);
}
