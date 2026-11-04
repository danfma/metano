import { type JsonContainer, MAX_JSON_DEPTH, readDepthMessage } from "./container-depth.ts";
import { JsonException } from "./errors.ts";
import { JsonNumberText } from "./exact-json-number.ts";

/**
 * Reads JSON text into the tree `JSON.parse` builds (#457), for
 * `JsonSerializer.deserializeText`:
 *
 * - `parseJsonText` is `JSON.parse`, throwing System.Text.Json's
 *   `JsonException` for invalid text instead of the engine's `SyntaxError`;
 * - `readJsonText` tokenizes the text itself and keeps every number with more
 *   significant digits than a double holds as a `JsonNumberText` leaf, so a
 *   `decimal` is built from its source digits.
 *
 * Both accept exactly what `JSON.parse` accepts (lone surrogates included,
 * which STJ rejects) and build the same tree: the last of duplicate keys
 * wins at the first key's position, and `__proto__` is an own property. The
 * tokenizer rejects what STJ's reader rejects with its message (measured on
 * .NET 10.0.12, without the `Path` suffix, DECIDED-5), in document order, and
 * a container nested past 64 levels anywhere in the document (FR-016).
 */

/** A number text with at most this many significant digits is exact as a double. */
const DOUBLE_EXACT_DIGITS = 15;

/** The shortest run of `[0-9.]` that can hold a mantissa of more than 15 digits. */
const INEXACT_RUN_LENGTH = DOUBLE_EXACT_DIGITS + 1;

const TAB = 0x09;
const LINE_FEED = 0x0a;
const CARRIAGE_RETURN = 0x0d;
const SPACE = 0x20;
const QUOTE = 0x22;
const PLUS = 0x2b;
const COMMA = 0x2c;
const MINUS = 0x2d;
const DOT = 0x2e;
const SLASH = 0x2f;
const ZERO = 0x30;
const NINE = 0x39;
const COLON = 0x3a;
const UPPER_E = 0x45;
const OPEN_BRACKET = 0x5b;
const BACKSLASH = 0x5c;
const CLOSE_BRACKET = 0x5d;
const LOWER_E = 0x65;
const LOWER_F = 0x66;
const LOWER_N = 0x6e;
const LOWER_T = 0x74;
const LOWER_U = 0x75;
const OPEN_BRACE = 0x7b;
const CLOSE_BRACE = 0x7d;
const LAST_PRINTABLE = 0x7e;

/** The characters `\` may escape besides `u`: `"`, `\`, `/`, `b`, `f`, `n`, `r`, `t`. */
const SIMPLE_ESCAPES = new Set([0x22, 0x5c, 0x2f, 0x62, 0x66, 0x6e, 0x72, 0x74]);

const UNICODE_ESCAPE_DIGITS = 4;

const NO_TOKENS =
  "The input does not contain any JSON tokens. Expected the input to start with a valid JSON " +
  "token, when isFinalBlock is true.";

const OPEN_CONTAINER =
  "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or " +
  "array that should be closed.";

const NOTHING_AFTER_COMMA =
  "Expected start of a property name or value, but instead reached end of data.";

const NOTHING_AFTER_PROPERTY_NAME = "Expected a value, but instead reached end of data.";

const UNTERMINATED_STRING = "Expected end of string, but instead reached end of data.";

const UNTERMINATED_NUMBER = "Expected a digit ('0'-'9'), but instead reached end of data.";

const SHOULD_BE_ESCAPED = "The string should be correctly escaped.";

const EXPECTED_DIGIT = "Expected a digit ('0'-'9').";

/**
 * Whether a number of `text` can have more than 15 significant digits. A
 * mantissa of 16 digits or more is a run of 16 `[0-9.]` characters or more,
 * and every such run covers an index that is 15 modulo 16, so only those
 * indexes are probed: about 5 ms on Node and 3 ms on Bun for 10 MB. A digit
 * run inside a string can answer `true` for a text without such a number,
 * which only costs the slower exact read.
 */
export function mayHoldInexactNumber(text: string): boolean {
  const { length } = text;

  for (let probe = INEXACT_RUN_LENGTH - 1; probe < length; probe += INEXACT_RUN_LENGTH) {
    if (isNumberRunCode(text.charCodeAt(probe)) && hasInexactRunAt(text, probe)) {
      return true;
    }
  }

  return false;
}

/** Whether the `[0-9.]` run through `index` is long enough to hold an inexact number. */
function hasInexactRunAt(text: string, index: number): boolean {
  let start = index;
  let end = index + 1;

  while (start > 0 && isNumberRunCode(text.charCodeAt(start - 1))) {
    start--;
  }

  while (end < text.length && isNumberRunCode(text.charCodeAt(end))) {
    end++;
  }

  return end - start >= INEXACT_RUN_LENGTH;
}

function isNumberRunCode(code: number): boolean {
  return isDigit(code) || code === DOT;
}

/** `JSON.parse(text)`, throwing System.Text.Json's `JsonException` for invalid text. */
export function parseJsonText(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch (error) {
    if (error instanceof SyntaxError) {
      // Throws the JsonException; should it accept the text, the engine's error stands.
      readJsonText(text);
    }

    throw error;
  }
}

/**
 * The tree `JSON.parse(text)` builds, except that a number with more than 15
 * significant digits is a `JsonNumberText` leaf. Strings are copied, so the
 * tree never keeps `text` alive (a slice would, on JavaScriptCore).
 */
export function readJsonText(text: string): unknown {
  return new JsonTextReader(text).readDocument();
}

/** A recursive-descent reader over the UTF-16 code units of one JSON document. */
class JsonTextReader {
  private position = 0;
  private depth = 0;

  constructor(private readonly text: string) {}

  readDocument(): unknown {
    const first = this.skipWhitespace();

    if (isEnd(first)) {
      throw new JsonException(NO_TOKENS);
    }

    const value = this.readValue(first);

    if (!isEnd(this.skipWhitespace())) {
      throw this.unexpected("is invalid after a single JSON value. Expected end of data.");
    }

    return value;
  }

  /** Reads the value whose first code, `code`, is at the current position. */
  private readValue(code: number): unknown {
    switch (code) {
      case QUOTE:
        return this.readString();
      case OPEN_BRACE:
        return this.readObject();
      case OPEN_BRACKET:
        return this.readArray();
      case LOWER_T:
        return this.readLiteral("true", true);
      case LOWER_F:
        return this.readLiteral("false", false);
      case LOWER_N:
        return this.readLiteral("null", null);
    }

    if (code === MINUS || isDigit(code)) {
      return this.readNumber();
    }

    throw this.unexpected("is an invalid start of a value.");
  }

  // ─── Containers ────────────────────────────────────────────────────────────

  private readObject(): Record<string, unknown> {
    const result: Record<string, unknown> = {};

    this.enterContainer("object");

    let code = this.codeInContainer();

    if (code === CLOSE_BRACE) {
      return this.exitContainer(result);
    }

    for (;;) {
      this.readMember(code, result);
      code = this.codeInContainer();

      if (code === CLOSE_BRACE) {
        return this.exitContainer(result);
      }

      if (code !== COMMA) {
        throw this.unexpectedAfterValue(CLOSE_BRACKET);
      }

      this.position++;
      code = this.codeAfterComma(CLOSE_BRACE, "object");
    }
  }

  /** Reads `"name": value` into `result`; `code`, the current one, is not the end. */
  private readMember(code: number, result: Record<string, unknown>): void {
    if (code !== QUOTE) {
      throw this.unexpected("is an invalid start of a property name. Expected a '\"'.");
    }

    const name = this.readString();
    const separator = this.skipWhitespace();

    if (isEnd(separator)) {
      throw new JsonException(NOTHING_AFTER_PROPERTY_NAME);
    }

    if (separator !== COLON) {
      throw this.unexpected("is invalid after a property name. Expected a ':'.");
    }

    this.position++;
    setMember(result, name, this.readValue(this.codeInContainer()));
  }

  private readArray(): unknown[] {
    const result: unknown[] = [];

    this.enterContainer("array");

    let code = this.codeInContainer();

    if (code === CLOSE_BRACKET) {
      return this.exitContainer(result);
    }

    for (;;) {
      result.push(this.readValue(code));
      code = this.codeInContainer();

      if (code === CLOSE_BRACKET) {
        return this.exitContainer(result);
      }

      if (code !== COMMA) {
        throw this.unexpectedAfterValue(CLOSE_BRACE);
      }

      this.position++;
      code = this.codeAfterComma(CLOSE_BRACKET, "array");
    }
  }

  /** The next code inside an open container, which the end of the text leaves open. */
  private codeInContainer(): number {
    const code = this.skipWhitespace();

    if (isEnd(code)) {
      throw new JsonException(OPEN_CONTAINER);
    }

    return code;
  }

  /** The next code after a comma: neither the end nor the container's `closer`. */
  private codeAfterComma(closer: number, container: JsonContainer): number {
    const code = this.skipWhitespace();

    if (isEnd(code)) {
      throw new JsonException(NOTHING_AFTER_COMMA);
    }

    if (code === closer) {
      throw new JsonException(trailingCommaMessage(container));
    }

    return code;
  }

  /** Steps over the opening `{` or `[`; the 65th nested container throws (FR-016). */
  private enterContainer(container: JsonContainer): void {
    if (this.depth >= MAX_JSON_DEPTH) {
      throw new JsonException(readDepthMessage(container));
    }

    this.depth++;
    this.position++;
  }

  /** Steps over the closing `}` or `]` and returns the container read. */
  private exitContainer<T>(container: T): T {
    this.depth--;
    this.position++;

    return container;
  }

  /** The error for the code after a value: the other container's closer has no matching open. */
  private unexpectedAfterValue(otherCloser: number): JsonException {
    if (this.text.charCodeAt(this.position) === otherCloser) {
      return this.unexpected("is invalid without a matching open.");
    }

    return this.unexpected("is invalid after a value. Expected either ',', '}', or ']'.");
  }

  // ─── Scalars ───────────────────────────────────────────────────────────────

  private readLiteral<T>(literal: string, value: T): T {
    const { text } = this;

    if (!text.startsWith(literal, this.position)) {
      throw new JsonException(
        `'${text.slice(this.position)}' is an invalid JSON literal. Expected the literal '${literal}'.`,
      );
    }

    this.position += literal.length;

    return value;
  }

  /** Reads the string at the opening quote, copied out of the text. */
  private readString(): string {
    const { text } = this;
    const start = this.position;
    let index = start + 1;
    let escaped = false;

    for (;;) {
      const code = text.charCodeAt(index);

      if (code === QUOTE) {
        break;
      }

      if (code === BACKSLASH) {
        index = this.escapeEnd(index);
        escaped = true;
        continue;
      }

      // NaN, past the end, fails the comparison too.
      if (!(code >= SPACE)) {
        throw this.invalidInString(index);
      }

      index++;
    }

    this.position = index + 1;

    return copiedString(text, start, this.position, escaped);
  }

  /** The index after the escape sequence whose backslash is at `index`. */
  private escapeEnd(index: number): number {
    const { text } = this;
    const code = text.charCodeAt(index + 1);

    if (SIMPLE_ESCAPES.has(code)) {
      return index + 2;
    }

    if (code !== LOWER_U) {
      throw this.unexpectedInString(
        index + 1,
        `is an invalid escapable character within a JSON string. ${SHOULD_BE_ESCAPED}`,
      );
    }

    for (let digit = index + 2; digit < index + 2 + UNICODE_ESCAPE_DIGITS; digit++) {
      if (!isHexDigit(text.charCodeAt(digit))) {
        throw this.unexpectedInString(
          digit,
          `is not a hex digit following '\\u' within a JSON string. ${SHOULD_BE_ESCAPED}`,
        );
      }
    }

    return index + 2 + UNICODE_ESCAPE_DIGITS;
  }

  /** A control character inside a string, or its end before the closing quote. */
  private invalidInString(index: number): JsonException {
    return this.unexpectedInString(index, `is invalid within a JSON string. ${SHOULD_BE_ESCAPED}`);
  }

  /** `message` about the code at `index`, or the unterminated string past the end. */
  private unexpectedInString(index: number, message: string): JsonException {
    if (index >= this.text.length) {
      return new JsonException(UNTERMINATED_STRING);
    }

    return new JsonException(`${printableCodeAt(this.text, index)} ${message}`);
  }

  /**
   * Reads the number at its first character with STJ's grammar: an optional
   * `-`, `0` or digits without a leading zero, an optional fraction and an
   * optional exponent, then a delimiter or the end.
   */
  private readNumber(): number | JsonNumberText {
    const { text } = this;
    const start = this.position;
    const signLength = text.charCodeAt(start) === MINUS ? 1 : 0;
    const integerEnd = this.integerEnd(start + signLength);
    const mantissaEnd = this.fractionEnd(integerEnd);
    const end = this.exponentEnd(mantissaEnd);

    this.throwIfNoDelimiterAt(end, mantissaEnd > integerEnd && end === mantissaEnd);
    this.position = end;

    if (end - start - signLength <= DOUBLE_EXACT_DIGITS) {
      return Number(text.slice(start, end));
    }

    return numberOf(text, start, start + signLength, mantissaEnd, end);
  }

  /** The end of the integer digits at `index`: `0`, or digits without a leading zero. */
  private integerEnd(index: number): number {
    const { text } = this;
    const code = text.charCodeAt(index);

    if (!isDigit(code)) {
      throw this.unexpectedInNumber(index, "immediately after a sign character ('+' or '-')");
    }

    if (code === ZERO) {
      if (isDigit(text.charCodeAt(index + 1))) {
        throw new JsonException(`Invalid leading zero before ${printableCodeAt(text, index + 1)}.`);
      }

      return index + 1;
    }

    return digitsEnd(text, index + 1);
  }

  /** The end of the fraction at `index`, if one starts there. */
  private fractionEnd(index: number): number {
    if (this.text.charCodeAt(index) !== DOT) {
      return index;
    }

    if (!isDigit(this.text.charCodeAt(index + 1))) {
      throw this.unexpectedInNumber(index + 1, "immediately after a decimal point ('.')");
    }

    return digitsEnd(this.text, index + 2);
  }

  /** The end of the exponent at `index`, if one starts there; STJ names its sign for a missing digit. */
  private exponentEnd(index: number): number {
    const { text } = this;
    const code = text.charCodeAt(index);

    if (code !== LOWER_E && code !== UPPER_E) {
      return index;
    }

    const sign = text.charCodeAt(index + 1);
    const firstDigit = sign === PLUS || sign === MINUS ? index + 2 : index + 1;

    if (!isDigit(text.charCodeAt(firstDigit))) {
      throw this.unexpectedInNumber(firstDigit, "immediately after a sign character ('+' or '-')");
    }

    return digitsEnd(text, firstDigit + 1);
  }

  /**
   * A number ends at a delimiter, or at the end of a root value; inside a
   * container STJ reports its last character at the end of the text.
   */
  private throwIfNoDelimiterAt(end: number, afterFraction: boolean): void {
    const { text } = this;

    if (end >= text.length) {
      if (this.depth > 0) {
        throw this.unexpectedEndOfNumber(end - 1, "Expected a delimiter.");
      }

      return;
    }

    if (!isDelimiter(text.charCodeAt(end))) {
      throw this.unexpectedEndOfNumber(
        end,
        afterFraction ? "Expected 'E' or 'e'." : "Expected a delimiter.",
      );
    }
  }

  private unexpectedEndOfNumber(index: number, expected: string): JsonException {
    return new JsonException(
      `${printableCodeAt(this.text, index)} is an invalid end of a number. ${expected}`,
    );
  }

  /** A missing digit at `index`, `where` in the number. */
  private unexpectedInNumber(index: number, where: string): JsonException {
    if (index >= this.text.length) {
      return new JsonException(UNTERMINATED_NUMBER);
    }

    return new JsonException(
      `${printableCodeAt(this.text, index)} is invalid within a number, ${where}. ${EXPECTED_DIGIT}`,
    );
  }

  // ─── Position ──────────────────────────────────────────────────────────────

  /** Skips JSON whitespace and returns the code there, `NaN` at the end. */
  private skipWhitespace(): number {
    const { text } = this;
    let index = this.position;
    let code = text.charCodeAt(index);

    while (isWhitespace(code)) {
      code = text.charCodeAt(++index);
    }

    this.position = index;

    return code;
  }

  /** `message` about the code at the current position. */
  private unexpected(message: string): JsonException {
    return new JsonException(`${printableCodeAt(this.text, this.position)} ${message}`);
  }
}

/** `charCodeAt` past the end of the text is `NaN`. */
function isEnd(code: number): boolean {
  return Number.isNaN(code);
}

function trailingCommaMessage(container: JsonContainer): string {
  return (
    `The JSON ${container} contains a trailing comma at the end which is not supported in this ` +
    "mode. Change the reader options."
  );
}

/** Sets a member as `JSON.parse` does: `__proto__` is an own property, not the prototype. */
function setMember(target: Record<string, unknown>, name: string, value: unknown): void {
  if (name === "__proto__") {
    Object.defineProperty(target, name, {
      value,
      writable: true,
      enumerable: true,
      configurable: true,
    });

    return;
  }

  target[name] = value;
}

/**
 * The string between the quotes at `start` and `end - 1`. `JSON.parse` of the
 * quoted slice both unescapes it and copies it out of `text`: on
 * JavaScriptCore a slice of any length keeps the whole text alive (measured:
 * a 10 MB payload stayed reachable through its 9-character values).
 */
function copiedString(text: string, start: number, end: number, escaped: boolean): string {
  if (!escaped && end - start <= 3) {
    // Empty and one-character strings are shared engine constants.
    return text.slice(start + 1, end - 1);
  }

  return JSON.parse(text.slice(start, end)) as string;
}

/**
 * The number `text` spells from `start` to `end`: a `number` when its
 * mantissa (from `mantissaStart` to `mantissaEnd`) has at most 15
 * significant digits, a `JsonNumberText` with a copy of its text otherwise.
 */
function numberOf(
  text: string,
  start: number,
  mantissaStart: number,
  mantissaEnd: number,
  end: number,
): number | JsonNumberText {
  const source = text.slice(start, end);

  if (significantDigitsOf(text, mantissaStart, mantissaEnd) <= DOUBLE_EXACT_DIGITS) {
    return Number(source);
  }

  return new JsonNumberText(JSON.parse(`"${source}"`) as string);
}

/** The digits of the mantissa from its first non-zero one on; trailing zeros count. */
function significantDigitsOf(text: string, start: number, end: number): number {
  let digits = 0;

  for (let index = start; index < end; index++) {
    const code = text.charCodeAt(index);
    const isSignificant = code !== DOT && (digits > 0 || code !== ZERO);

    if (isSignificant) {
      digits++;
    }
  }

  return digits;
}

function digitsEnd(text: string, index: number): number {
  let end = index;

  while (isDigit(text.charCodeAt(end))) {
    end++;
  }

  return end;
}

function isDigit(code: number): boolean {
  return code >= ZERO && code <= NINE;
}

function isHexDigit(code: number): boolean {
  return isDigit(code) || ((code | 0x20) >= 0x61 && (code | 0x20) <= 0x66);
}

function isWhitespace(code: number): boolean {
  return code === SPACE || code === LINE_FEED || code === CARRIAGE_RETURN || code === TAB;
}

/** What may follow a number: STJ's delimiters, `/` included. */
function isDelimiter(code: number): boolean {
  return (
    code === COMMA ||
    code === CLOSE_BRACE ||
    code === CLOSE_BRACKET ||
    code === SLASH ||
    isWhitespace(code)
  );
}

/**
 * The code at `index` as STJ prints the byte there: a printable ASCII
 * character itself (`'x'`), anything else as the first byte of its UTF-8
 * encoding (`'0x0A'`, `'0xC3'` for `é`, `'0xF0'` for an emoji).
 */
function printableCodeAt(text: string, index: number): string {
  const code = text.charCodeAt(index);

  if (code >= SPACE && code <= LAST_PRINTABLE) {
    return `'${String.fromCharCode(code)}'`;
  }

  return `'0x${firstUtf8ByteOf(text.codePointAt(index)!).toString(16).toUpperCase().padStart(2, "0")}'`;
}

function firstUtf8ByteOf(codePoint: number): number {
  if (codePoint < 0x80) {
    return codePoint;
  }

  if (codePoint < 0x800) {
    return 0xc0 | (codePoint >> 6);
  }

  return codePoint < 0x10000 ? 0xe0 | (codePoint >> 12) : 0xf0 | (codePoint >> 18);
}
