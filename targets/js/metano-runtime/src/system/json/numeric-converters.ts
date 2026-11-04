import type { DecimalDescriptor, JsonReadOptions, PrimitiveDescriptor } from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { JsonException } from "./errors.ts";
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
 * A `System.Decimal`: written with `toNumber()`, read from a JSON number, or
 * from a JSON string when the context reads numbers from strings, within the
 * decimal range. The descriptor's `type` (decimal.js `Decimal` in generated
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

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return (value as { toNumber(): number }).toNumber();
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;

      if (!isDecimalValue(value)) {
        throw cannotConvert("System.Decimal", errorTypeName);
      }

      return descriptor.type ? new descriptor.type(value) : value;
    },
  };
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
