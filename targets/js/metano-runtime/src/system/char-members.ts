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
