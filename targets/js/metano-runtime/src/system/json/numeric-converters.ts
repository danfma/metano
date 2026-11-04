import type { DecimalDescriptor, JsonReadOptions, PrimitiveDescriptor } from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { JsonException } from "./errors.ts";
import { rawJsonTextOf, writeExactNumber } from "./exact-json-number.ts";
import { isDecimalText, readNumberText } from "./number-from-string.ts";

/**
 * The built-in converters of numeric descriptors: `primitive` (numbers read
 * from JSON strings when the context allows it and the descriptor names its
 * `numberType`) and `decimal` (FR-048).
 */

/** How a rejected value names its CLR type in the conversion error. */
export type ErrorTypeName = (clrName: string) => string;

/** The CLR name itself: `System.Int32`. */
export const declaredTypeName: ErrorTypeName = (clrName) => clrName;

/** System.Text.Json names a nullable number ``System.Nullable`1[System.Int32]``. */
export const nullableTypeName: ErrorTypeName = (clrName) => `System.Nullable\`1[${clrName}]`;

/**
 * `System.Decimal.MaxValue` as the double `JSON.parse` rounds it to (2^96).
 * A JSON number is already a double, so it is compared with this, not by
 * its text: `79228162514264337593543950335`, `…335.4` and
 * `7.9228162514264337593543950335e28` all read as MaxValue in
 * System.Text.Json and all parse to this double (measured).
 */
const DECIMAL_MAX_AS_DOUBLE = Number("79228162514264337593543950335");

const passthroughConverter: ValueConverter = {
  serialize: (value: unknown) => value,
  deserialize: (value: unknown) => value,
};

/**
 * A `primitive`: values pass through both ways, except that a JSON string
 * for a numeric `numberType` is read by System.Text.Json's grammar when the
 * context reads numbers from strings, and a rejected one throws its
 * conversion error. Writes are unaffected (FR-048 item 5).
 */
export function primitiveConverterOf(
  descriptor: PrimitiveDescriptor,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  const { numberType } = descriptor;

  if (numberType === undefined || !readsNumbersFromStrings(readOptions)) {
    return passthroughConverter;
  }

  return {
    serialize: passthroughConverter.serialize,
    deserialize: (value: unknown) => {
      if (typeof value !== "string") {
        return value;
      }

      const number = readNumberText(value, numberType);

      if (number === undefined) {
        throw cannotConvert(numberType, errorTypeName);
      }

      return number;
    },
  };
}

/**
 * A `System.Decimal`: written with its exact digits (see `decimalJsonOf`;
 * a raw value read without a descriptor `type` is written back as is), read
 * from a JSON number, a `JSON.rawJSON` leaf `serialize` wrote, or a JSON
 * string when the context reads numbers from strings, within the decimal
 * range. The descriptor's `type` (decimal.js `Decimal` in generated
 * contexts) builds the value from the JSON number or the text, so a string
 * keeps its significant digits (decimal.js drops trailing zeros); without
 * it the JSON value is returned as is. A JSON number is range-checked as the
 * double it already is. Anything else throws System.Text.Json's conversion
 * error.
 */
export function decimalConverterOf(
  descriptor: DecimalDescriptor,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  const readsStrings = readsNumbersFromStrings(readOptions);

  const isDecimalValue = (value: unknown): value is string | number =>
    (typeof value === "number" && isDecimalNumber(value)) ||
    (typeof value === "string" && readsStrings && isDecimalText(value));

  /** A leaf `serialize` wrote, read from its exact text. */
  const readRawJsonNumber = (leaf: unknown, text: string): unknown => {
    if (!isDecimalText(text)) {
      throw cannotConvert("System.Decimal", errorTypeName);
    }

    return descriptor.type ? new descriptor.type(text) : leaf;
  };

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return isDecimalLike(value) ? decimalJsonOf(value) : value;
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;

      const rawText = rawJsonTextOf(value);

      if (rawText !== undefined) {
        return readRawJsonNumber(value, rawText);
      }

      if (!isDecimalValue(value)) {
        throw cannotConvert("System.Decimal", errorTypeName);
      }

      return descriptor.type ? new descriptor.type(value) : value;
    },
  };
}

/** The decimal.js surface the writer uses. */
type DecimalLike = {
  toNumber(): number;
  toFixed?(): string;
  isZero?(): boolean;
};

/**
 * A decimal.js-like object. What a descriptor without `type` reads (a JSON
 * number, string or `JSON.rawJSON` leaf) is not one, so it is written back
 * unchanged; a `number` has a `toFixed` of its own, hence the object test.
 */
function isDecimalLike(value: unknown): value is DecimalLike {
  return typeof value === "object" && typeof (value as DecimalLike).toNumber === "function";
}

/**
 * decimal.js `toFixed()` without arguments prints every digit it holds in
 * plain notation, which is what System.Text.Json writes for the same value
 * (`79228162514264337593543950335`, `0.0000001`; `-0` prints `0`). The
 * scale is not kept: decimal.js holds `1.10` as `1.1` (measured). A value
 * without `toFixed`, or an engine without `JSON.rawJSON`, writes
 * `toNumber()`.
 *
 * A value no `System.Decimal` holds (NaN, ±Infinity, beyond `MaxValue`)
 * throws before any text is built, so `1e400000000` costs no 400 MB string.
 * A non-zero value below the smallest double is written `0`: System.Text.Json
 * reads `1e-400` as `0` (measured), and its plain digits would be as long as
 * its exponent.
 */
function decimalJsonOf(value: DecimalLike): unknown {
  const approximate = value.toNumber();

  if (!isWithinDecimalRange(value, approximate)) {
    throw new JsonException(
      `The value ${String(value)} cannot be written as System.Decimal: it is outside the System.Decimal range.`,
    );
  }

  if (typeof value.toFixed !== "function") {
    return approximate;
  }

  const text = isBelowDoubleRange(value, approximate) ? "0" : value.toFixed();

  return writeExactNumber(text, () => approximate);
}

/**
 * Compared as a double first; only a value that rounds to the bound itself
 * (`79228162514264337593543950335` and its neighbors) is decided by its
 * exact text, with the read grammar, so every leaf written is read back.
 * NaN fails both comparisons.
 */
function isWithinDecimalRange(value: DecimalLike, approximate: number): boolean {
  const magnitude = Math.abs(approximate);

  if (magnitude !== DECIMAL_MAX_AS_DOUBLE) {
    return magnitude < DECIMAL_MAX_AS_DOUBLE;
  }

  return isDecimalText(value.toFixed?.() ?? String(approximate));
}

/** A non-zero value whose double underflows to zero. */
function isBelowDoubleRange(value: DecimalLike, approximate: number): boolean {
  return approximate === 0 && value.isZero?.() === false;
}

/** A finite JSON number within the decimal range, compared as doubles. */
function isDecimalNumber(value: number): boolean {
  return Math.abs(value) <= DECIMAL_MAX_AS_DOUBLE;
}

function readsNumbersFromStrings(readOptions: JsonReadOptions | undefined): boolean {
  return readOptions?.numberHandling === "allowReadingFromString";
}

function cannotConvert(clrName: string, errorTypeName: ErrorTypeName): JsonException {
  return new JsonException(`The JSON value could not be converted to ${errorTypeName(clrName)}.`);
}
