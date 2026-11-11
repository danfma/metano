/**
 * `decimal.Parse(text)` in the invariant culture with .NET's default
 * `NumberStyles.Number`: white space around the number, a leading or trailing
 * sign, group separators and a decimal point. The compiler lowers it to
 * `new Decimal(parseDecimalText(text))`: this function checks .NET's grammar and
 * range, so a malformed text throws `FormatException` (not decimal.js's
 * `DecimalError`), and returns the text decimal.js reads.
 */
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { FormatException } from "./exceptions/format-exception.ts";
import { OverflowException } from "./exceptions/overflow-exception.ts";

/** .NET's white space around a number: U+0009–U+000D and U+0020. */
const WHITE_SPACE = "[\\t\\n\\v\\f\\r ]*";

/** `NumberStyles.Number`, capturing the leading sign, the body and the trailing sign. */
const NUMBER = new RegExp(
  `^${WHITE_SPACE}([+-]?)(\\d[\\d,]*(?:\\.\\d*)?|\\.\\d+)(?:${WHITE_SPACE}([+-]))?${WHITE_SPACE}$`,
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

  const parts = NUMBER.exec(text.replace(/\0+$/, ""));
  const [, leadingSign = "", body = "", trailingSign = ""] = parts ?? [];

  if (parts === null || (leadingSign !== "" && trailingSign !== "")) {
    throw new FormatException(`The input string '${text}' was not in a correct format.`);
  }

  const [integer = "", fraction = ""] = body.replaceAll(",", "").split(".");

  if (BigInt(integer || "0") > DECIMAL_MAX) {
    throw new OverflowException(DECIMAL_RANGE_MESSAGE);
  }

  const sign = leadingSign === "-" || trailingSign === "-" ? "-" : "";

  return `${sign}${integer || "0"}${fraction ? `.${fraction}` : ""}`;
}
