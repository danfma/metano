/**
 * Runtime helpers for the C# `TryParse(string, out T)` family on the primitive types. The
 * compiler lowers each `out` parameter into the second tuple element (see the declarative
 * mappings under `Metano/Runtime/Parsing.cs`), so `int.TryParse(s, out var n)` becomes
 * `const [ok, n] = tryParseInt32(s)`.
 *
 * Parsing follows the invariant culture: `.` is the decimal separator, `,` the thousands
 * separator, and only the whitespace .NET accepts around a number (tab, line feed,
 * vertical tab, form feed, carriage return and space) is ignored. On failure the value is
 * C#'s `default` for the type (`0` or `false`).
 */

/** The result of a `TryParse` call: whether it succeeded and the parsed value. */
export type ParseResult<T> = readonly [found: boolean, value: T];

const INT32_MIN = -2147483648;
const INT32_MAX = 2147483647;
const INT64_MIN = -9223372036854775808n;
const INT64_MAX = 9223372036854775807n;

/** `NumberStyles.Integer`: an optional sign followed by decimal digits. */
const INTEGER = /^[+-]?\d+$/;

/**
 * `NumberStyles.Float | NumberStyles.AllowThousands`: an optional sign, digits with
 * optional thousands separators, an optional fraction and an optional exponent.
 */
const FLOAT = /^[+-]?(?:\d[\d,]*(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;

/** The symbols `double.Parse` accepts for the special values, compared case-insensitively. */
const SPECIAL_FLOATS = new Map<string, number>([
  ["nan", Number.NaN],
  ["infinity", Number.POSITIVE_INFINITY],
  ["+infinity", Number.POSITIVE_INFINITY],
  ["-infinity", Number.NEGATIVE_INFINITY],
  ["∞", Number.POSITIVE_INFINITY],
  ["+∞", Number.POSITIVE_INFINITY],
  ["-∞", Number.NEGATIVE_INFINITY],
]);

/** Mirrors `int.TryParse(string, out int)`. */
export function tryParseInt32(text: string | null | undefined): ParseResult<number> {
  const integer = parseInteger(text);

  return integer !== null && integer >= INT32_MIN && integer <= INT32_MAX
    ? [true, Number(integer)]
    : [false, 0];
}

/**
 * Mirrors `long.TryParse(string, out long)`. The range check is exact; the value is a
 * JavaScript `number`, so magnitudes above `Number.MAX_SAFE_INTEGER` lose precision, as
 * every `long` does in the generated code.
 */
export function tryParseInt64(text: string | null | undefined): ParseResult<number> {
  const integer = parseInteger(text);

  return integer !== null && integer >= INT64_MIN && integer <= INT64_MAX
    ? [true, Number(integer)]
    : [false, 0];
}

/**
 * Mirrors `double.TryParse(string, out double)` (and `float.TryParse`, whose values the
 * generated code holds as `number` too). A value beyond the `double` range parses to
 * an infinity, as it does since .NET Core 3.0.
 */
export function tryParseFloat64(text: string | null | undefined): ParseResult<number> {
  const trimmed = trimNumberWhitespace(text);

  if (trimmed === null) {
    return [false, 0];
  }

  const special = SPECIAL_FLOATS.get(trimmed.toLowerCase());

  if (special !== undefined) {
    return [true, special];
  }

  return FLOAT.test(trimmed) ? [true, Number(trimmed.replace(/,/g, ""))] : [false, 0];
}

/**
 * Mirrors `bool.TryParse(string, out bool)`: `true` / `false` in any letter case, around
 * whitespace (JavaScript's `\s`, close to .NET's `char.IsWhiteSpace`) and `\0` characters.
 */
export function tryParseBoolean(text: string | null | undefined): ParseResult<boolean> {
  const word = text?.replace(/^[\s\0]+|[\s\0]+$/g, "").toLowerCase();

  if (word === "true") {
    return [true, true];
  }

  return [word === "false", false];
}

/** The integer `text` spells, or `null` when it is not an integer. */
function parseInteger(text: string | null | undefined): bigint | null {
  const trimmed = trimNumberWhitespace(text);

  return trimmed !== null && INTEGER.test(trimmed) ? BigInt(trimmed) : null;
}

/**
 * `text` without the whitespace .NET allows around a number (and the trailing `\0`
 * characters it tolerates), or `null` when nothing is left.
 */
function trimNumberWhitespace(text: string | null | undefined): string | null {
  const trimmed = text?.replace(/^[\t\n\v\f\r ]+|[\t\n\v\f\r \0]+$/g, "") ?? "";

  return trimmed.length > 0 ? trimmed : null;
}
