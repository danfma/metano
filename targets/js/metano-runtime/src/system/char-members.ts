/**
 * `System.Char` category tests (`char.IsLetter`, ...) over a one-code-unit string, which is
 * how a `char` is represented. The tests read Unicode general categories, as .NET does, so a
 * lone surrogate is in no category. The Unicode version is the JavaScript engine's, which can
 * differ from .NET's for recently assigned code points.
 */

const LETTER = /^\p{L}$/u;
const DIGIT = /^\p{Nd}$/u;
const LETTER_OR_DIGIT = /^[\p{L}\p{Nd}]$/u;
const NUMBER = /^\p{N}$/u;
const UPPER = /^\p{Lu}$/u;
const LOWER = /^\p{Ll}$/u;
const PUNCTUATION = /^\p{P}$/u;
const SYMBOL = /^\p{S}$/u;
const SEPARATOR = /^\p{Z}$/u;
const CONTROL = /^\p{Cc}$/u;
/** U+0009-U+000D and U+0085 are white space in .NET beyond the Unicode separators (`\s` also admits U+FEFF). */
const WHITE_SPACE = /^[\p{Z}\t-\r\u0085]$/u;

/** `char.IsLetter(c)`. */
export function charIsLetter(c: string): boolean {
  return LETTER.test(c);
}

/** `char.IsDigit(c)`: a decimal digit of any script. */
export function charIsDigit(c: string): boolean {
  return DIGIT.test(c);
}

/** `char.IsLetterOrDigit(c)`. */
export function charIsLetterOrDigit(c: string): boolean {
  return LETTER_OR_DIGIT.test(c);
}

/** `char.IsNumber(c)`: a decimal digit, a letter number or another number. */
export function charIsNumber(c: string): boolean {
  return NUMBER.test(c);
}

/** `char.IsUpper(c)`. */
export function charIsUpper(c: string): boolean {
  return UPPER.test(c);
}

/** `char.IsLower(c)`. */
export function charIsLower(c: string): boolean {
  return LOWER.test(c);
}

/** `char.IsPunctuation(c)`. */
export function charIsPunctuation(c: string): boolean {
  return PUNCTUATION.test(c);
}

/** `char.IsSymbol(c)`. */
export function charIsSymbol(c: string): boolean {
  return SYMBOL.test(c);
}

/** `char.IsSeparator(c)`. */
export function charIsSeparator(c: string): boolean {
  return SEPARATOR.test(c);
}

/** `char.IsControl(c)`. */
export function charIsControl(c: string): boolean {
  return CONTROL.test(c);
}

/** `char.IsWhiteSpace(c)`. */
export function charIsWhiteSpace(c: string): boolean {
  return WHITE_SPACE.test(c);
}

/** `char.IsAscii(c)`: a code unit below U+0080. */
export function charIsAscii(c: string): boolean {
  return c.charCodeAt(0) < 0x80;
}

/** `char.IsAsciiDigit(c)`: `0`-`9`. */
export function charIsAsciiDigit(c: string): boolean {
  return c >= "0" && c <= "9";
}

/** `char.IsAsciiLetterLower(c)`: `a`-`z`. */
export function charIsAsciiLetterLower(c: string): boolean {
  return c >= "a" && c <= "z";
}

/** `char.IsAsciiLetterUpper(c)`: `A`-`Z`. */
export function charIsAsciiLetterUpper(c: string): boolean {
  return c >= "A" && c <= "Z";
}

/** `char.IsAsciiLetter(c)`: `a`-`z` or `A`-`Z`. */
export function charIsAsciiLetter(c: string): boolean {
  return charIsAsciiLetterLower(c) || charIsAsciiLetterUpper(c);
}

/** `char.IsAsciiLetterOrDigit(c)`. */
export function charIsAsciiLetterOrDigit(c: string): boolean {
  return charIsAsciiLetter(c) || charIsAsciiDigit(c);
}

/** `char.IsAsciiHexDigitLower(c)`: `0`-`9` or `a`-`f`. */
export function charIsAsciiHexDigitLower(c: string): boolean {
  return charIsAsciiDigit(c) || (c >= "a" && c <= "f");
}

/** `char.IsAsciiHexDigitUpper(c)`: `0`-`9` or `A`-`F`. */
export function charIsAsciiHexDigitUpper(c: string): boolean {
  return charIsAsciiDigit(c) || (c >= "A" && c <= "F");
}

/** `char.IsAsciiHexDigit(c)`: `0`-`9`, `a`-`f` or `A`-`F`. */
export function charIsAsciiHexDigit(c: string): boolean {
  return charIsAsciiHexDigitLower(c) || charIsAsciiHexDigitUpper(c);
}

/**
 * `char.IsBetween(c, min, max)`: .NET computes `(uint)(c - min) <= (uint)(max - min)`
 * by code unit, so the range wraps around when `min > max`.
 */
export function charIsBetween(c: string, min: string, max: string): boolean {
  const start = min.charCodeAt(0);
  const offset = (c.charCodeAt(0) - start) >>> 0;
  const width = (max.charCodeAt(0) - start) >>> 0;

  return offset <= width;
}
