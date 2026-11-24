/**
 * `decimal.Parse(text)` in the invariant culture with .NET's default
 * `NumberStyles.Number`: white space around the number, a leading or trailing
 * sign, group separators and a decimal point. The compiler lowers it to
 * `parseDecimal(text, Decimal)`: this module checks .NET's grammar and
 * range, so a malformed text throws `FormatException` (not decimal.js's
 * `DecimalError`), and keeps presentation scale outside the decimal.js value.
 */
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { FormatException } from "./exceptions/format-exception.ts";
import { OverflowException } from "./exceptions/overflow-exception.ts";
import { lazyValue } from "./lazy-value.ts";
import { withDecimalScale } from "./default-text.ts";

/** .NET's white space around a number: U+0009–U+000D and U+0020. */
const WHITE_SPACE = "[\\t\\n\\v\\f\\r ]*";

/** `NumberStyles.Number`, capturing the leading sign, the body and the trailing sign. */
const NUMBER = /* @__PURE__ */ lazyValue(
  () =>
    new RegExp(
      `^${WHITE_SPACE}([+-]?)(\\d[\\d,]*(?:\\.\\d*)?|\\.\\d+)(?:${WHITE_SPACE}([+-]))?${WHITE_SPACE}$`,
    ),
);

/** The largest integer part a `decimal` holds: `decimal.MaxValue`. */
const DECIMAL_MAX = 79228162514264337593543950335n;

const DECIMAL_RANGE_MESSAGE = "Value was either too large or too small for a Decimal.";

/**
 * The canonical text of the `decimal` `text` denotes.
 *
 * @throws ArgumentNullException for `null`.
 * @throws FormatException for text outside .NET's `NumberStyles.Number` grammar.
 * @throws OverflowException for a value beyond `decimal.MaxValue` in magnitude.
 */
export function parseDecimalText(text: string | null | undefined): string {
  if (text == null) {
    throw new ArgumentNullException("s");
  }

  const parts = NUMBER().exec(text.replace(/\0+$/, ""));

  if (parts === null) {
    throw formatException(text);
  }

  const [, leadingSign = "", body = "", trailingSign = ""] = parts;

  if (leadingSign !== "" && trailingSign !== "") {
    throw formatException(text);
  }

  const [integer = "", fraction = ""] = body.replaceAll(",", "").split(".");

  if (BigInt(integer || "0") > DECIMAL_MAX) {
    throw new OverflowException(DECIMAL_RANGE_MESSAGE);
  }

  const sign = leadingSign === "-" || trailingSign === "-" ? "-" : "";

  return `${sign}${integer || "0"}${fraction ? `.${fraction}` : ""}`;
}

/** The largest scale of a `decimal`. */
const DECIMAL_SCALE_MAX = 28;

/** `1e0` … `1e28`, parsed so each is the exact double .NET's table holds. */
const DOUBLE_POWERS_OF_TEN = /* @__PURE__ */ lazyValue(() =>
  Array.from({ length: DECIMAL_SCALE_MAX + 1 }, (_, power) => Number(`1e${power}`)),
);

/** A scratch view that reads the biased binary exponent of a `number`. */
const EXPONENT_VIEW = /* @__PURE__ */ lazyValue(() => new DataView(new ArrayBuffer(8)));

/**
 * The digits of `(decimal)d` (`digits` 15) or `(decimal)f` (`digits` 7), for
 * `new Decimal(…)`, ported from .NET's `VarDecFromR8` / `VarDecFromR4`
 * (#695): the value is scaled by a power of ten to `digits` significant
 * digits, the scale capped at 28, rounded half to even, and trailing zeros
 * are dropped from the scale. So `(decimal)(0.1 + 0.2)` is `0.3`,
 * `(decimal)1000000000000005.0` is `1000000000000000` and `(decimal)1e-30`
 * is `0`. The scaling is done in double arithmetic, as .NET does.
 *
 * @throws OverflowException for NaN, ±Infinity and a magnitude of 2^96 or more.
 */
export function decimalTextFromDouble(value: number, digits: 7 | 15): string {
  const exponent = binaryExponentOf(value);

  if (exponent < -94) {
    return "0";
  }

  if (exponent > 96) {
    throw new OverflowException(DECIMAL_RANGE_MESSAGE);
  }

  const lowest = DOUBLE_POWERS_OF_TEN()[digits - 1]!;
  let power = digits - 1 - ((exponent * 19728) >> 16);
  let scaled = Math.abs(value);

  if (power >= 0) {
    power = Math.min(power, DECIMAL_SCALE_MAX);
    scaled *= DOUBLE_POWERS_OF_TEN()[power]!;
  } else if (power !== -1 || scaled >= lowest * 10) {
    scaled /= DOUBLE_POWERS_OF_TEN()[-power]!;
  } else {
    power = 0;
  }

  if (scaled < lowest && power < DECIMAL_SCALE_MAX) {
    scaled *= 10;
    power++;
  }

  const mantissa = roundHalfToEven(scaled);

  if (mantissa === 0) {
    return "0";
  }

  const sign = value < 0 ? "-" : "";

  return power < 0
    ? `${sign}${mantissa}${"0".repeat(-power)}`
    : `${sign}${scaledText(String(mantissa), power, digits - 1)}`;
}

/**
 * The exponent .NET reads from a binary floating-point value: the biased
 * exponent of the double minus 1022, so `2^e ≤ |value| < 2^(e+1)` gives
 * `e + 1`. A `float` held as a double reads the same exponent.
 */
function binaryExponentOf(value: number): number {
  const view = EXPONENT_VIEW();

  view.setFloat64(0, value);

  return ((view.getUint16(0) >> 4) & 0x7ff) - 1022;
}

/** `value` (below 2^53) rounded to an integer, a tie to the even one. */
function roundHalfToEven(value: number): number {
  const floor = Math.floor(value);
  const rest = value - floor;

  return rest > 0.5 || (rest === 0.5 && floor % 2 === 1) ? floor + 1 : floor;
}

/**
 * `digitsText` divided by `10^scale`, written out after up to `maxStrip`
 * trailing zeros are dropped from the scale.
 */
function scaledText(digitsText: string, scale: number, maxStrip: number): string {
  let text = digitsText;
  let remaining = scale;
  let strippable = Math.min(scale, maxStrip);

  while (strippable > 0 && text.endsWith("0")) {
    text = text.slice(0, -1);
    remaining--;
    strippable--;
  }

  if (remaining === 0) {
    return text;
  }

  const padded = text.padStart(remaining + 1, "0");

  return `${padded.slice(0, -remaining)}.${padded.slice(-remaining)}`;
}

function formatException(text: string): FormatException {
  return new FormatException(`The input string '${text}' was not in a correct format.`);
}

/** Parses once and records scale without decorating the constructed numeric value. */
export function parseDecimal<TDecimal extends object>(
  text: string | null | undefined,
  create: new (value: string) => TDecimal,
): TDecimal {
  const normalized = parseDecimalText(text);
  const point = normalized.indexOf(".");
  const value = new create(normalized);

  return point < 0 ? value : withDecimalScale(value, normalized.length - point - 1);
}

/**
 * `decimal.TryParse(text, out value)` (the `(string, out decimal)` and `(string,
 * IFormatProvider, out decimal)` overloads): `[true, value]` for text {@link parseDecimalText}
 * accepts, `[false, 0]` for anything else, `null` included. `create` is the `Decimal` class
 * the generated code imports from `metano-runtime/system/decimal`, so this barrel module does
 * not import decimal.js (a project that never uses `decimal` does not load it).
 */
export function tryParseDecimal<TDecimal extends object>(
  text: string | null | undefined,
  create: new (value: string) => TDecimal,
): readonly [found: boolean, value: TDecimal] {
  try {
    return [true, parseDecimal(text, create)];
  } catch (error) {
    if (error instanceof FormatException || error instanceof OverflowException || text == null) {
      return [false, new create("0")];
    }

    throw error;
  }
}
