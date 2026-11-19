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
