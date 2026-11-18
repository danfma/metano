/**
 * Runtime helpers for the C# `Parse(string)` and `TryParse(string, out T)` families on the
 * primitive types. The compiler lowers each `out` parameter into the second tuple element
 * (see the declarative mappings under `Metano/Runtime/Parsing.cs`), so
 * `int.TryParse(s, out var n)` becomes `const [ok, n] = tryParseInt32(s)`, and `int.Parse(s)`
 * becomes `parseInt32(s)`, which throws .NET's exceptions where `Number.parseInt` returns
 * `12` for `"12abc"`.
 *
 * Parsing follows the invariant culture: `.` is the decimal separator, `,` the thousands
 * separator, and only the whitespace .NET accepts around a number (tab, line feed,
 * vertical tab, form feed, carriage return and space) is ignored. On failure the value is
 * C#'s `default` for the type (`0` or `false`). The integer parsers take the `NumberStyles`
 * flags the compiler folds from the C# argument, as `long.Parse` does (`int64-parsing.ts`).
 */
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { FormatException } from "./exceptions/format-exception.ts";
import {
  DEFAULT_INTEGER_STYLE,
  parseInRange,
  tryParseInRange,
  type IntegerRange,
} from "./int64-parsing.ts";

/** The result of a `TryParse` call: whether it succeeded and the parsed value. */
export type ParseResult<T> = readonly [found: boolean, value: T];

/**
 * `NumberStyles.Float | NumberStyles.AllowThousands`: an optional sign, digits with
 * optional thousands separators, an optional fraction and an optional exponent.
 */
const FLOAT = /^[+-]?(?:\d[\d,]*(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;

/** The symbols `double.Parse` accepts for the special values, compared case-insensitively. */
const SPECIAL_FLOATS = /* @__PURE__ */ new Map<string, number>([
  ["nan", NaN],
  ["infinity", Infinity],
  ["+infinity", Infinity],
  ["-infinity", -Infinity],
  ["∞", Infinity],
  ["+∞", Infinity],
  ["-∞", -Infinity],
]);

/** The integer type of `bits` bits, signed or not, named `typeName` in .NET's overflow message. */
function integerRange(bits: number, signed: boolean, typeName: string): IntegerRange {
  const span = 1n << BigInt(bits);

  return {
    min: signed ? -(span >> 1n) : 0n,
    max: signed ? (span >> 1n) - 1n : span - 1n,
    bits,
    message: `Value was either too large or too small for ${typeName}.`,
    family: "integers",
  };
}

const INT32 = /* @__PURE__ */ integerRange(32, true, "an Int32");
const UINT32 = /* @__PURE__ */ integerRange(32, false, "a UInt32");
const INT16 = /* @__PURE__ */ integerRange(16, true, "an Int16");
const UINT16 = /* @__PURE__ */ integerRange(16, false, "a UInt16");
const BYTE = /* @__PURE__ */ integerRange(8, false, "an unsigned byte");
const SBYTE = /* @__PURE__ */ integerRange(8, true, "a signed byte");

/**
 * Mirrors `int.Parse(string)` and its `NumberStyles` overloads.
 *
 * @throws ArgumentNullException for `null`.
 * @throws FormatException for text outside the style's grammar.
 * @throws OverflowException for a value outside `int`.
 */
export function parseInt32(text: string | null | undefined, style = DEFAULT_INTEGER_STYLE): number {
  return Number(parseInRange(text, style, INT32));
}

/** Mirrors `uint.Parse(string)` and its `NumberStyles` overloads. */
export function parseUInt32(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): number {
  return Number(parseInRange(text, style, UINT32));
}

/** Mirrors `short.Parse(string)` and its `NumberStyles` overloads. */
export function parseInt16(text: string | null | undefined, style = DEFAULT_INTEGER_STYLE): number {
  return Number(parseInRange(text, style, INT16));
}

/** Mirrors `ushort.Parse(string)` and its `NumberStyles` overloads. */
export function parseUInt16(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): number {
  return Number(parseInRange(text, style, UINT16));
}

/** Mirrors `byte.Parse(string)` and its `NumberStyles` overloads. */
export function parseByte(text: string | null | undefined, style = DEFAULT_INTEGER_STYLE): number {
  return Number(parseInRange(text, style, BYTE));
}

/** Mirrors `sbyte.Parse(string)` and its `NumberStyles` overloads. */
export function parseSByte(text: string | null | undefined, style = DEFAULT_INTEGER_STYLE): number {
  return Number(parseInRange(text, style, SBYTE));
}

/** Mirrors `int.TryParse(string, out int)` and its `NumberStyles` overloads. */
export function tryParseInt32(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, INT32));
}

/** Mirrors `uint.TryParse(string, out uint)` and its `NumberStyles` overloads. */
export function tryParseUInt32(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, UINT32));
}

/** Mirrors `short.TryParse(string, out short)` and its `NumberStyles` overloads. */
export function tryParseInt16(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, INT16));
}

/** Mirrors `ushort.TryParse(string, out ushort)` and its `NumberStyles` overloads. */
export function tryParseUInt16(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, UINT16));
}

/** Mirrors `byte.TryParse(string, out byte)` and its `NumberStyles` overloads. */
export function tryParseByte(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, BYTE));
}

/** Mirrors `sbyte.TryParse(string, out sbyte)` and its `NumberStyles` overloads. */
export function tryParseSByte(
  text: string | null | undefined,
  style = DEFAULT_INTEGER_STYLE,
): ParseResult<number> {
  return numberResult(tryParseInRange(text, style, SBYTE));
}

function numberResult(result: readonly [found: boolean, value: bigint]): ParseResult<number> {
  return [result[0], Number(result[1])];
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

/**
 * Mirrors `double.Parse(string)` (and `float.Parse`, whose values the generated code holds as
 * `number` too).
 *
 * @throws ArgumentNullException for `null`.
 * @throws FormatException for text outside the grammar of {@link tryParseFloat64}.
 */
export function parseFloat64(text: string | null | undefined): number {
  if (text == null) {
    throw new ArgumentNullException("s");
  }

  const [found, value] = tryParseFloat64(text);

  if (!found) {
    throw new FormatException(`The input string '${text}' was not in a correct format.`);
  }

  return value;
}

/**
 * Mirrors `bool.Parse(string)`.
 *
 * @throws ArgumentNullException for `null`.
 * @throws FormatException for text that is neither `true` nor `false`.
 */
export function parseBoolean(text: string | null | undefined): boolean {
  if (text == null) {
    throw new ArgumentNullException("value");
  }

  const [found, value] = tryParseBoolean(text);

  if (!found) {
    throw new FormatException(`String '${text}' was not recognized as a valid Boolean.`);
  }

  return value;
}

/**
 * `text` without the whitespace .NET allows around a number (and the trailing `\0`
 * characters it tolerates), or `null` when nothing is left.
 */
function trimNumberWhitespace(text: string | null | undefined): string | null {
  const trimmed = text?.replace(/^[\t\n\v\f\r ]+|[\t\n\v\f\r \0]+$/g, "") ?? "";

  return trimmed.length > 0 ? trimmed : null;
}
