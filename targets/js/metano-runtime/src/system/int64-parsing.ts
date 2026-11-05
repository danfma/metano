/**
 * `long.Parse` / `ulong.Parse` and their `TryParse` twins in the invariant
 * culture, for the `NumberStyles` the compiler supports: `Integer` (the
 * default), `HexNumber`, `Integer | AllowThousands` and `Number`. The grammar
 * is .NET's, not `BigInt(string)`'s, which accepts `""`, `0x` prefixes and
 * other whitespace. The compiler folds the `NumberStyles` argument to its
 * flags value.
 */
import {
  ArgumentNullException,
  FormatException,
  NotSupportedException,
  OverflowException,
} from "./exceptions/index.ts";
import { INT64_MAX, INT64_MIN, UINT64_MAX } from "./int64.ts";

/** The result of a `TryParse` call: whether it succeeded and the parsed value (`0n` on failure). */
export type Int64ParseResult = readonly [found: boolean, value: bigint];

/** `NumberStyles.Integer`: leading and trailing white space and a leading sign. */
const INTEGER_STYLE = 7;

/** `NumberStyles.HexNumber`: white space around hex digits. */
const HEX_NUMBER_STYLE = 515;

/** `NumberStyles.Integer | NumberStyles.AllowThousands`. */
const INTEGER_THOUSANDS_STYLE = 71;

/** `NumberStyles.Number`: also a trailing sign, group separators and a decimal point. */
const NUMBER_STYLE = 111;

/** A 64-bit hex value has at most 16 significant digits. */
const MAX_HEX_DIGITS = 16;

const INT64_RANGE_MESSAGE = "Value was either too large or too small for an Int64.";

const UINT64_RANGE_MESSAGE = "Value was either too large or too small for a UInt64.";

/** .NET's white space around a number: U+0009–U+000D and U+0020 (not U+00A0). */
const WHITE_SPACE = "[\\t\\n\\v\\f\\r ]*";

/**
 * The grammar of each supported style, capturing the leading sign, the body
 * and the trailing sign. Trailing `\0` characters are removed first, as .NET
 * ignores them.
 */
const GRAMMARS = new Map<number, RegExp>([
  [INTEGER_STYLE, grammar("([+-]?)(\\d+)()")],
  [HEX_NUMBER_STYLE, grammar("()([0-9A-Fa-f]+)()")],
  [INTEGER_THOUSANDS_STYLE, grammar("([+-]?)(\\d[\\d,]*)()")],
  [NUMBER_STYLE, grammar(`([+-]?)(\\d[\\d,]*(?:\\.\\d*)?|\\.\\d+)(?:${WHITE_SPACE}([+-]))?`)],
]);

/** The parts of a number the grammars capture. */
type ParsedNumber = {
  readonly negative: boolean;
  readonly integer: bigint;
  readonly hasDecimalPoint: boolean;
  readonly fractionIsZero: boolean;
};

/** What a parse produced: a value, or the exception `Parse` throws and `TryParse` reports as `false`. */
type ParseOutcome = { readonly value: bigint } | { readonly error: Error };

/** The bounds and message of the target type. */
type Int64Range = { readonly min: bigint; readonly max: bigint; readonly message: string };

const SIGNED: Int64Range = { min: INT64_MIN, max: INT64_MAX, message: INT64_RANGE_MESSAGE };

const UNSIGNED: Int64Range = { min: 0n, max: UINT64_MAX, message: UINT64_RANGE_MESSAGE };

/**
 * `long.Parse(text, style)`.
 *
 * @throws ArgumentNullException for `null`.
 * @throws FormatException for text outside the style's grammar.
 * @throws OverflowException for a value outside `long`, or a non-zero fraction.
 * @throws NotSupportedException for a style the runtime does not implement.
 */
export function parseInt64(text: string | null | undefined, style: number = INTEGER_STYLE): bigint {
  return parsedValue(parse(text, style, SIGNED));
}

/** `ulong.Parse(text, style)`; `-0` is `0n`, any other negative value overflows. */
export function parseUInt64(
  text: string | null | undefined,
  style: number = INTEGER_STYLE,
): bigint {
  return parsedValue(parse(text, style, UNSIGNED));
}

/**
 * `long.TryParse(text, style, out value)`: `[false, 0n]` wherever
 * {@link parseInt64} throws. Named apart from the 2.x `number` `tryParseInt64`
 * until the `bigint` mapping is the only one (spec 012 slice 012k).
 */
export function tryParseInt64Value(
  text: string | null | undefined,
  style: number = INTEGER_STYLE,
): Int64ParseResult {
  return parseResult(parse(text, style, SIGNED));
}

/** `ulong.TryParse(text, style, out value)`: `[false, 0n]` wherever {@link parseUInt64} throws. */
export function tryParseUInt64(
  text: string | null | undefined,
  style: number = INTEGER_STYLE,
): Int64ParseResult {
  return parseResult(parse(text, style, UNSIGNED));
}

function parsedValue(outcome: ParseOutcome): bigint {
  if ("error" in outcome) {
    throw outcome.error;
  }

  return outcome.value;
}

function parseResult(outcome: ParseOutcome): Int64ParseResult {
  return "error" in outcome ? [false, 0n] : [true, outcome.value];
}

function parse(text: string | null | undefined, style: number, range: Int64Range): ParseOutcome {
  const pattern = GRAMMARS.get(style);

  if (pattern === undefined) {
    throw new NotSupportedException(
      `NumberStyles value ${style} is not supported for 64-bit integers in TypeScript.`,
    );
  }

  if (text == null) {
    return { error: new ArgumentNullException("s") };
  }

  const parts = pattern.exec(text.replace(/\0+$/, ""));
  const [, leadingSign = "", body = "", trailingSign = ""] = parts ?? [];
  const signedTwice = leadingSign !== "" && trailingSign !== "";

  if (parts === null || signedTwice) {
    return {
      error: new FormatException(`The input string '${text}' was not in a correct format.`),
    };
  }

  if (style === HEX_NUMBER_STYLE) {
    return hexValue(body, range);
  }

  return decimalValue(parsedNumber(body, leadingSign === "-" || trailingSign === "-"), range);
}

/** The decimal digits of `body` (group separators dropped) and whether its fraction is all zeros. */
function parsedNumber(body: string, negative: boolean): ParsedNumber {
  const [integer = "", fraction = ""] = body.replaceAll(",", "").split(".");

  return {
    negative,
    integer: integer === "" ? 0n : BigInt(integer),
    hasDecimalPoint: body.includes("."),
    fractionIsZero: /^0*$/.test(fraction),
  };
}

function decimalValue(number: ParsedNumber, range: Int64Range): ParseOutcome {
  const value = number.negative ? -number.integer : number.integer;
  const negativeZeroOfUnsigned = number.negative && range.min === 0n && number.hasDecimalPoint;
  const fits = value >= range.min && value <= range.max && !negativeZeroOfUnsigned;

  return fits && number.fractionIsZero
    ? { value }
    : { error: new OverflowException(range.message) };
}

/** Up to 16 significant hex digits, read as two's complement for `long`. */
function hexValue(digits: string, range: Int64Range): ParseOutcome {
  const significant = digits.replace(/^0+/, "");

  if (significant.length > MAX_HEX_DIGITS) {
    return { error: new OverflowException(range.message) };
  }

  const unsigned = BigInt(`0x${significant || "0"}`);

  return { value: range.min === 0n ? unsigned : BigInt.asIntN(64, unsigned) };
}

/** A whole-string grammar: white space, the number, white space. */
function grammar(number: string): RegExp {
  return new RegExp(`^${WHITE_SPACE}${number}${WHITE_SPACE}$`);
}
