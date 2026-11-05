import type { DecimalDescriptor, JsonReadOptions, PrimitiveDescriptor } from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { JsonException } from "./errors.ts";
import { isDecimalText, readDecimalText } from "./decimal-text.ts";
import { exactNumberTextOf, numberOfTreeValue, writeExactNumber } from "./exact-json-number.ts";
import { readNumberText } from "./number-from-string.ts";

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

/**
 * A double prints at most 17 significant digits, so one of at least
 * `1e-11` has none past the 28th decimal place; only a smaller non-zero one
 * is rounded by the decimal's scale (`1e-29` reads as `0`, `1.5e-28` as
 * `2e-28`).
 */
const SMALLEST_UNROUNDED_DOUBLE = 1e-11;

const passthroughConverter: ValueConverter = {
  serialize: (value: unknown) => value,
  deserialize: (value: unknown) => value,
};

/** The CLR name of a C# `char`, which reads as a one-code-unit `string`. */
const CHAR_TYPE_NAME = "System.Char";

/**
 * A `primitive`: values pass through both ways, except that a JSON string
 * for a numeric `numberType` is read by System.Text.Json's grammar when the
 * context reads numbers from strings, and a rejected one throws its
 * conversion error; a `char` reads a one-code-unit `string` only (#471).
 * Writes are unaffected (FR-048 item 5).
 */
export function primitiveConverterOf(
  descriptor: PrimitiveDescriptor,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  const { numberType } = descriptor;

  if (descriptor.typeName === CHAR_TYPE_NAME) {
    return charConverterOf(errorTypeName);
  }

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
 * A `System.Decimal`.
 *
 * - Written with its exact digits (`decimalJsonOf`); a raw value read
 *   without a descriptor `type` is written back as is.
 * - Read as System.Text.Json reads it (`decimal-text.ts`: rounded to 28
 *   decimal places and 96 bits, out of range past `MaxValue`) from:
 *   - the exact text of a number leaf: a `JsonNumberText` read by
 *     `deserializeText`, or a `JSON.rawJSON` leaf `serialize` wrote (#457);
 *   - a JSON number, range-checked as the double it already is;
 *   - a JSON string, when the context reads numbers from strings.
 * - The descriptor's `type` (decimal.js `Decimal` in generated contexts)
 *   builds the value from that text, or from the number itself when no
 *   digit rounds away; without it the JSON value is returned as is (a
 *   `JsonNumberText` as the `number` `JSON.parse` gives).
 * - Anything else throws System.Text.Json's conversion error.
 */
export function decimalConverterOf(
  descriptor: DecimalDescriptor,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  const readsStrings = readsNumbersFromStrings(readOptions);
  const { type } = descriptor;

  const notADecimal = () => cannotConvert("System.Decimal", errorTypeName);

  /** The decimal `text` holds, or, without `type`, the JSON value it came from. */
  const decimalOfText = (text: string, jsonValue: unknown): unknown => {
    const decimalText = readDecimalText(text);

    if (decimalText === undefined) {
      throw notADecimal();
    }

    return type ? new type(decimalText) : jsonValue;
  };

  const decimalOfNumber = (value: number): unknown => {
    if (!isDecimalNumber(value)) {
      throw notADecimal();
    }

    if (!type) return value;

    return roundsAwayDigits(value) ? decimalOfText(String(value), value) : new type(value);
  };

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return isDecimalLike(value) ? decimalJsonOf(value) : value;
    },
    deserialize: (value: unknown) => {
      // A value type: JSON `null` throws, as System.Text.Json throws (#471).
      if (value == null) throw notADecimal();

      if (typeof value === "number") return decimalOfNumber(value);

      if (typeof value === "string" && readsStrings) return decimalOfText(value, value);

      const exactText = exactNumberTextOf(value);

      if (exactText === undefined) {
        throw notADecimal();
      }

      return decimalOfText(exactText, numberOfTreeValue(value));
    },
  };
}

/**
 * The decimal.js surface the writer uses. `e` is decimal.js's exponent of
 * the most significant digit (`NaN` for NaN and ±Infinity).
 */
type DecimalLike = {
  toNumber(): number;
  toFixed?(): string;
  readonly e?: number;
};

/**
 * Where a value sits against the `System.Decimal` domain: `outside` it,
 * at its `edge` (decided by the exact text), `inside` it, or `negligible`
 * (non-zero, but too small for its plain digits to be worth printing).
 */
type DecimalPosition = "outside" | "edge" | "inside" | "negligible";

/** `MaxValue` is `7.92…e28`: an exponent of 28 may or may not be in range. */
const DECIMAL_MAX_EXPONENT = 28;

/**
 * System.Text.Json reads `1e-29` and anything smaller as `0` (measured:
 * `1e-29`, `5e-29`, `1e-40`, `1e-400`). A value below `1e-29` (exponent
 * under -29) is written `0`; one from `1e-29` up keeps its digits.
 */
const DECIMAL_SMALLEST_EXPONENT = -29;

/** `1e-29`, the same bound for a value compared as a double. */
const DECIMAL_SMALLEST_AS_DOUBLE = 1e-29;

/**
 * The largest exponent the domain error prints in plain notation; past it
 * the error keeps decimal.js's exponent form, so `1e400000000` costs no
 * 400 MB message.
 */
const PLAIN_ERROR_MAX_EXPONENT = 64;

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
 * A negligible one is written `0` on every path (exponent or double, with or
 * without `JSON.rawJSON`) rather than as its exponent's worth of zeros.
 */
function decimalJsonOf(value: DecimalLike): unknown {
  const position = decimalPositionOf(value);

  if (position === "outside" || (position === "edge" && !isDecimalText(exactTextOf(value)))) {
    throw new JsonException(
      `The value ${errorTextOf(value)} cannot be written as System.Decimal: it is outside the System.Decimal range.`,
    );
  }

  if (typeof value.toFixed !== "function") {
    return position === "negligible" ? 0 : value.toNumber();
  }

  if (position === "negligible") {
    return writeExactNumber("0", () => 0);
  }

  return writeExactNumber(value.toFixed(), () => value.toNumber());
}

/**
 * From decimal.js's exponent when the value has one, a field read with no
 * digits converted; otherwise from its double, compared with 2^96.
 */
function decimalPositionOf(value: DecimalLike): DecimalPosition {
  const { e } = value;

  return typeof e === "number" ? positionOfExponent(e) : positionOfDouble(value);
}

function positionOfExponent(exponent: number): DecimalPosition {
  if (!(exponent <= DECIMAL_MAX_EXPONENT)) return "outside";

  if (exponent === DECIMAL_MAX_EXPONENT) return "edge";

  return exponent < DECIMAL_SMALLEST_EXPONENT ? "negligible" : "inside";
}

/** NaN fails every comparison, so it is `outside`. */
function positionOfDouble(value: DecimalLike): DecimalPosition {
  const magnitude = Math.abs(value.toNumber());

  if (magnitude === DECIMAL_MAX_AS_DOUBLE) return "edge";

  if (!(magnitude < DECIMAL_MAX_AS_DOUBLE)) return "outside";

  return magnitude < DECIMAL_SMALLEST_AS_DOUBLE ? "negligible" : "inside";
}

/**
 * The exact text of a value at the edge, read with the read grammar, so
 * every leaf written is one `deserialize` accepts (`…335.4` is written and
 * STJ reads it as `MaxValue`; `…335.5` and `…336` throw).
 */
function exactTextOf(value: DecimalLike): string {
  return value.toFixed?.() ?? String(value.toNumber());
}

/**
 * The rejected value in plain notation (`100000000000000000000000000000`
 * for `1e29`), as long as its exponent is small enough to print; `NaN` and
 * `Infinity` print as such. A value without decimal.js's exponent prints
 * with `String()`, since the length of its `toFixed()` is unknown.
 */
function errorTextOf(value: DecimalLike): string {
  const { e } = value;
  const printsPlain = typeof e === "number" && !(e > PLAIN_ERROR_MAX_EXPONENT);

  return printsPlain ? (value.toFixed?.() ?? String(value)) : String(value);
}

/** A finite JSON number within the decimal range, compared as doubles. */
function isDecimalNumber(value: number): boolean {
  return Math.abs(value) <= DECIMAL_MAX_AS_DOUBLE;
}

function roundsAwayDigits(value: number): boolean {
  return value !== 0 && Math.abs(value) < SMALLEST_UNROUNDED_DOUBLE;
}

function readsNumbersFromStrings(readOptions: JsonReadOptions | undefined): boolean {
  return readOptions?.numberHandling === "allowReadingFromString";
}

/**
 * A `char` (#471, measured on .NET 10): exactly one UTF-16 code unit of a
 * JSON string — `"ab"` and a surrogate pair are rejected, as are the other
 * JSON kinds — naming the type of the position in the conversion error.
 */
function charConverterOf(errorTypeName: ErrorTypeName): ValueConverter {
  const notAChar = () => cannotConvert(CHAR_TYPE_NAME, errorTypeName);

  return {
    serialize: passthroughConverter.serialize,
    deserialize: (value: unknown) => {
      if (typeof value !== "string" || value.length !== 1) {
        throw notAChar();
      }

      return value;
    },
  };
}

function cannotConvert(clrName: string, errorTypeName: ErrorTypeName): JsonException {
  return new JsonException(`The JSON value could not be converted to ${errorTypeName(clrName)}.`);
}
