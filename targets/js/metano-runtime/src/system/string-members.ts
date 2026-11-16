/**
 * Runtime helpers for the `System.String` members whose C# arguments or results differ
 * from the closest JavaScript built-in (#661): `Substring(start, length)` takes a length
 * where `substring` takes an end index, `Remove` / `Insert` / `PadLeft` / `ToCharArray`
 * have no JavaScript namesake, `IndexOf` / `LastIndexOf` / `Split` / `Trim` take counts,
 * start indexes, character sets and comparisons that JavaScript's methods read
 * differently, and every one of them throws .NET's exceptions for a bad argument where
 * JavaScript clamps or swaps. The compiler maps each member here declaratively
 * (`Metano/Runtime/Strings.cs`); every helper reads its arguments once.
 *
 * Comparisons are ordinal. The culture-sensitive `StringComparison` members behave as
 * their ordinal counterparts (JavaScript has no culture-aware comparison), and a
 * case-insensitive comparison folds each UTF-16 code unit by its invariant upper-case
 * form, as `OrdinalIgnoreCase` does.
 */

import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ArgumentNullException } from "./exceptions/argument-null-exception.ts";
import { ArgumentOutOfRangeException } from "./exceptions/argument-out-of-range-exception.ts";
import { isDotNetWhiteSpace } from "./strings.ts";

/**
 * Mirror of `System.StringComparison`. The numeric values match the .NET enum, so a
 * value read from serialized C# data keeps its meaning.
 */
export const StringComparison = {
  CurrentCulture: 0,
  CurrentCultureIgnoreCase: 1,
  InvariantCulture: 2,
  InvariantCultureIgnoreCase: 3,
  Ordinal: 4,
  OrdinalIgnoreCase: 5,
} as const;

export type StringComparison = (typeof StringComparison)[keyof typeof StringComparison];

/**
 * Mirror of `System.StringSplitOptions` (a flags enum). The numeric values match the
 * .NET enum.
 */
export const StringSplitOptions = {
  None: 0,
  RemoveEmptyEntries: 1,
  TrimEntries: 2,
} as const;

export type StringSplitOptions = (typeof StringSplitOptions)[keyof typeof StringSplitOptions];

/** One character a trim removes, or an array of them (a `char[]` argument spread). */
export type TrimCharacters = string | readonly string[];

/** The separators a split accepts: one character or string, or a list of them. */
export type SeparatorSet = string | readonly string[] | null | undefined;

const REMOVE_EMPTY_ENTRIES = 1;
const TRIM_ENTRIES = 2;

const INDEX_OUT_OF_RANGE_MESSAGE =
  "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
const COUNT_OUT_OF_RANGE_MESSAGE =
  "Count must be positive and count must refer to a location within the string/array/collection.";

/** `true` for the `StringComparison` members that ignore case. */
function ignoresCase(comparison: StringComparison | undefined): boolean {
  return (
    comparison === StringComparison.CurrentCultureIgnoreCase ||
    comparison === StringComparison.InvariantCultureIgnoreCase ||
    comparison === StringComparison.OrdinalIgnoreCase
  );
}

/**
 * `value` with each UTF-16 code unit replaced by its invariant upper-case form when that
 * form is a single unit, so the result has the length of `value` and an index into one
 * is an index into the other.
 */
function foldCase(value: string): string {
  let folded = "";

  for (let index = 0; index < value.length; index++) {
    const unit = value[index] as string;
    const upper = unit.toUpperCase();

    folded += upper.length === 1 ? upper : unit;
  }

  return folded;
}

function comparable(value: string, comparison: StringComparison | undefined): string {
  return ignoresCase(comparison) ? foldCase(value) : value;
}

function requireValue(value: string | null | undefined, paramName: string): string {
  if (value == null) {
    throw new ArgumentNullException(paramName);
  }

  return value;
}

function outOfRange(
  paramName: string,
  message: string,
  actualValue?: number,
): ArgumentOutOfRangeException {
  return new ArgumentOutOfRangeException(paramName, actualValue, message);
}

function requireNonNegative(paramName: string, value: number): void {
  if (value < 0) {
    throw outOfRange(paramName, `${paramName} ('${value}') must be a non-negative value.`, value);
  }
}

/**
 * Throws unless `startIndex` is inside `value` (`0` to its length). .NET reads a negative
 * index as the unsigned number it wraps to (`4294967295` for `-1`) in this message.
 */
function requireStartIndexInside(value: string, startIndex: number): void {
  const unsigned = startIndex >>> 0;

  if (unsigned > value.length) {
    throw outOfRange(
      "startIndex",
      `startIndex ('${unsigned}') must be less than or equal to '${value.length}'.`,
      unsigned,
    );
  }
}

/**
 * C#'s `value.Substring(startIndex)` and `value.Substring(startIndex, length)`: the
 * `length` characters from `startIndex`, or the rest of the string.
 *
 * @throws ArgumentOutOfRangeException when an argument is negative or the range ends
 *   beyond the string.
 */
export function stringSubstring(value: string, startIndex: number, length?: number): string {
  requireNonNegative("startIndex", startIndex);

  if (startIndex > value.length) {
    throw outOfRange("startIndex", "startIndex cannot be larger than length of string.");
  }

  if (length === undefined) {
    return value.slice(startIndex);
  }

  requireNonNegative("length", length);

  if (length > value.length - startIndex) {
    throw outOfRange("length", "Index and length must refer to a location within the string.");
  }

  return value.slice(startIndex, startIndex + length);
}

/**
 * C#'s `value.Remove(startIndex)` and `value.Remove(startIndex, count)`: the string
 * without the `count` characters from `startIndex`, or without everything from it.
 *
 * @throws ArgumentOutOfRangeException when an argument is negative or the range ends
 *   beyond the string.
 */
export function stringRemove(value: string, startIndex: number, count?: number): string {
  if (count === undefined) {
    if (startIndex < 0) {
      throw outOfRange("startIndex", "StartIndex cannot be less than zero.");
    }

    if (startIndex > value.length) {
      throw outOfRange("startIndex", "startIndex cannot be larger than length of string.");
    }

    return value.slice(0, startIndex);
  }

  requireNonNegative("startIndex", startIndex);
  requireNonNegative("count", count);

  if (count > value.length - startIndex) {
    throw outOfRange(
      "count",
      `count ('${count}') must be less than or equal to '${value.length - startIndex}'.`,
      count,
    );
  }

  return value.slice(0, startIndex) + value.slice(startIndex + count);
}

/**
 * C#'s `value.Insert(startIndex, text)`.
 *
 * @throws ArgumentNullException when `text` is `null`.
 * @throws ArgumentOutOfRangeException when `startIndex` is outside the string.
 */
export function stringInsert(value: string, startIndex: number, text: string): string {
  requireValue(text, "value");

  requireStartIndexInside(value, startIndex);

  return value.slice(0, startIndex) + text + value.slice(startIndex);
}

/**
 * C#'s `value.PadLeft(totalWidth)` and `value.PadLeft(totalWidth, paddingChar)`.
 *
 * @throws ArgumentOutOfRangeException when `totalWidth` is negative.
 */
export function stringPadLeft(value: string, totalWidth: number, paddingChar = " "): string {
  requireNonNegative("totalWidth", totalWidth);

  return value.padStart(totalWidth, paddingChar);
}

/**
 * C#'s `value.PadRight(totalWidth)` and `value.PadRight(totalWidth, paddingChar)`.
 *
 * @throws ArgumentOutOfRangeException when `totalWidth` is negative.
 */
export function stringPadRight(value: string, totalWidth: number, paddingChar = " "): string {
  requireNonNegative("totalWidth", totalWidth);

  return value.padEnd(totalWidth, paddingChar);
}

/**
 * C#'s `value.IndexOf(text)` family: `(text)`, `(text, startIndex)`,
 * `(text, startIndex, count)` and the same with a trailing `StringComparison`. The
 * search covers `count` characters from `startIndex`, and the result is an index into
 * `value`.
 *
 * @throws ArgumentNullException when `text` is `null`.
 * @throws ArgumentOutOfRangeException when `startIndex` or `count` is outside the string.
 */
export function stringIndexOf(
  value: string,
  text: string,
  startIndex = 0,
  count?: number,
  comparison?: StringComparison,
): number {
  requireValue(text, "value");

  if (startIndex < 0 || startIndex > value.length) {
    throw outOfRange("startIndex", INDEX_OUT_OF_RANGE_MESSAGE);
  }

  const window = count ?? value.length - startIndex;

  if (window < 0 || window > value.length - startIndex) {
    throw outOfRange("count", COUNT_OUT_OF_RANGE_MESSAGE);
  }

  const found = comparable(value, comparison).indexOf(comparable(text, comparison), startIndex);

  return found !== -1 && found + text.length <= startIndex + window ? found : -1;
}

/**
 * C#'s `value.LastIndexOf(text)` family: `(text)`, `(text, startIndex)`,
 * `(text, startIndex, count)` and the same with a trailing `StringComparison`. Unlike
 * JavaScript's `lastIndexOf`, the search starts at `startIndex` and runs backwards over
 * `count` characters, and a match must lie wholly inside that window.
 *
 * @throws ArgumentNullException when `text` is `null`.
 * @throws ArgumentOutOfRangeException when `startIndex` or `count` is outside the string.
 */
export function stringLastIndexOf(
  value: string,
  text: string,
  startIndex?: number,
  count?: number,
  comparison?: StringComparison,
): number {
  requireValue(text, "value");

  if (value.length === 0) {
    return text.length === 0 ? 0 : -1;
  }

  const last = startIndex === undefined ? value.length - 1 : Math.min(startIndex, value.length - 1);

  if (startIndex !== undefined && (startIndex < 0 || startIndex > value.length)) {
    throw outOfRange(
      "startIndex",
      "Index was out of range. Must be non-negative and less than the size of the collection.",
    );
  }

  const window = count ?? last + 1;

  if (window < 0 || window > last + 1) {
    throw outOfRange("count", COUNT_OUT_OF_RANGE_MESSAGE);
  }

  if (text.length === 0) {
    return startIndex === undefined && count === undefined ? value.length : last + 1;
  }

  const lowest = last - window + 1;
  const latestStart = last - text.length + 1;

  if (latestStart < lowest) {
    return -1;
  }

  const found = comparable(value, comparison).lastIndexOf(
    comparable(text, comparison),
    latestStart,
  );

  return found >= lowest ? found : -1;
}

/**
 * C#'s `value.StartsWith(prefix, comparison)`.
 *
 * @throws ArgumentNullException when `prefix` is `null`.
 */
export function stringStartsWith(
  value: string,
  prefix: string,
  comparison: StringComparison,
): boolean {
  requireValue(prefix, "value");

  return comparable(value, comparison).startsWith(comparable(prefix, comparison));
}

/**
 * C#'s `value.EndsWith(suffix, comparison)`.
 *
 * @throws ArgumentNullException when `suffix` is `null`.
 */
export function stringEndsWith(
  value: string,
  suffix: string,
  comparison: StringComparison,
): boolean {
  requireValue(suffix, "value");

  return comparable(value, comparison).endsWith(comparable(suffix, comparison));
}

/**
 * C#'s `value.Contains(text, comparison)`.
 *
 * @throws ArgumentNullException when `text` is `null`.
 */
export function stringContains(value: string, text: string, comparison: StringComparison): boolean {
  requireValue(text, "value");

  return comparable(value, comparison).includes(comparable(text, comparison));
}

/** The characters a trim removes: the given ones, or white space when there are none. */
function trimPredicate(characters: readonly TrimCharacters[]): (unit: string) => boolean {
  const set = characters.flat();

  return set.length === 0
    ? (unit) => isDotNetWhiteSpace(unit.charCodeAt(0))
    : (unit) => set.includes(unit);
}

function trimmed(
  value: string,
  characters: readonly TrimCharacters[],
  atStart: boolean,
  atEnd: boolean,
): string {
  const removes = trimPredicate(characters);
  let start = 0;
  let end = value.length;

  while (atStart && start < end && removes(value[start] as string)) {
    start++;
  }

  while (atEnd && end > start && removes(value[end - 1] as string)) {
    end--;
  }

  return value.slice(start, end);
}

/**
 * C#'s `value.Trim(trimChar)` / `value.Trim(params char[])`: removes the given
 * characters from both ends, or .NET's white space when none are given. The characters
 * come as separate arguments or as arrays of them.
 */
export function stringTrim(value: string, ...characters: readonly TrimCharacters[]): string {
  return trimmed(value, characters, true, true);
}

/** C#'s `value.TrimStart(trimChar)` / `value.TrimStart(params char[])`. */
export function stringTrimStart(value: string, ...characters: readonly TrimCharacters[]): string {
  return trimmed(value, characters, true, false);
}

/** C#'s `value.TrimEnd(trimChar)` / `value.TrimEnd(params char[])`. */
export function stringTrimEnd(value: string, ...characters: readonly TrimCharacters[]): string {
  return trimmed(value, characters, false, true);
}

/**
 * The separator found at `position`, as its length; `0` when none matches. A `null`
 * `separators` list stands for white space (`Split()` without separators); a string
 * list matches in order and ignores empty entries.
 */
function separatorLengthAt(
  value: string,
  position: number,
  separators: readonly string[] | null,
): number {
  if (separators === null) {
    return isDotNetWhiteSpace(value.charCodeAt(position)) ? 1 : 0;
  }

  for (const separator of separators) {
    if (separator.length > 0 && value.startsWith(separator, position)) {
      return separator.length;
    }
  }

  return 0;
}

/** The position after the separators that start at `position` (`position` when none does). */
function skipSeparators(
  value: string,
  position: number,
  separators: readonly string[] | null,
): number {
  let next = position;
  let length = separatorLengthAt(value, next, separators);

  while (length > 0) {
    next += length;
    length = separatorLengthAt(value, next, separators);
  }

  return next;
}

/**
 * The separators a `Split` argument means: a character or string is one separator, an
 * array is a list of them. A `null` or empty array stands for white space; a `null` or
 * empty single string for no separator at all, as in .NET.
 */
function separatorsOf(separator: SeparatorSet, isArray: boolean): readonly string[] | null {
  if (typeof separator === "string") {
    return [separator];
  }

  if (separator == null || separator.length === 0) {
    return isArray ? null : [];
  }

  return separator;
}

/**
 * C#'s `value.Split(...)`: one helper for the `char`, `char[]`, `string` and `string[]`
 * separator overloads, with or without a `count` and `StringSplitOptions`. Unlike
 * JavaScript's `split(separator, limit)`, the last piece keeps the rest of the string
 * when `count` is reached.
 *
 * `isArray` is `true` for the `char[]` and `string[]` overloads, where a missing or empty
 * list splits at white space; the single `string` overload splits nowhere then.
 *
 * @throws ArgumentOutOfRangeException when `count` is negative.
 */
export function stringSplit(
  value: string,
  separator: SeparatorSet,
  isArray: boolean,
  count = Number.MAX_SAFE_INTEGER,
  options: StringSplitOptions = StringSplitOptions.None,
): string[] {
  requireNonNegative("count", count);

  if (count === 0) {
    return [];
  }

  const separators = separatorsOf(separator, isArray);
  const removeEmpty = (options & REMOVE_EMPTY_ENTRIES) !== 0;
  const trimEntries = (options & TRIM_ENTRIES) !== 0;
  const pieces: string[] = [];

  const add = (piece: string): void => {
    const entry = trimEntries ? stringTrim(piece) : piece;

    if (!removeEmpty || entry.length > 0) {
      pieces.push(entry);
    }
  };

  let pieceStart = 0;
  let position = 0;

  while (position < value.length && pieces.length < count - 1) {
    const length = separatorLengthAt(value, position, separators);

    if (length === 0) {
      position++;
    } else {
      add(value.slice(pieceStart, position));
      position += length;
      pieceStart = position;
    }
  }

  // A split into one piece returns the string whole; a longer one skips the empty entries
  // that precede its last piece.
  if (removeEmpty && count > 1) {
    pieceStart = skipSeparators(value, pieceStart, separators);
  }

  add(value.slice(pieceStart));

  return pieces;
}

/**
 * C#'s `value.Split(params char[])`: splits at any of the given characters, or at white
 * space when there are none. The characters come as separate arguments or as arrays of
 * them.
 */
export function stringSplitChars(
  value: string,
  ...separators: readonly TrimCharacters[]
): string[] {
  return stringSplit(value, separators.flat(), true);
}

/**
 * C#'s `value.ToCharArray()` and `value.ToCharArray(startIndex, length)`: the UTF-16 code
 * units, each as a one-character string.
 *
 * @throws ArgumentOutOfRangeException when the range is outside the string.
 */
export function stringToCharArray(value: string, startIndex = 0, length?: number): string[] {
  requireStartIndexInside(value, startIndex);

  const window = length ?? value.length - startIndex;

  requireNonNegative("length", window);

  if (startIndex > value.length - window) {
    throw outOfRange(
      "startIndex",
      `startIndex ('${startIndex}') must be less than or equal to '${value.length - window}'.`,
      startIndex,
    );
  }

  return value.slice(startIndex, startIndex + window).split("");
}

/**
 * C#'s `string.Join(separator, values, startIndex, count)`: joins `count` strings from
 * `startIndex`. A `null` separator joins with no text.
 *
 * @throws ArgumentOutOfRangeException when `startIndex` or `count` is outside the array.
 */
export function stringJoinRange(
  separator: string | null,
  values: readonly (string | null | undefined)[],
  startIndex: number,
  count: number,
): string {
  if (values == null) {
    throw new ArgumentNullException("value");
  }

  requireNonNegative("startIndex", startIndex);
  requireNonNegative("count", count);

  if (startIndex > values.length - count) {
    throw outOfRange(
      "startIndex",
      `startIndex ('${startIndex}') must be less than or equal to '${values.length - count}'.`,
      startIndex,
    );
  }

  return values.slice(startIndex, startIndex + count).join(separator ?? "");
}

/**
 * C#'s `value.Replace(oldValue, newValue)` for the `(char, char)` and
 * `(string, string?)` overloads, and `value.Replace(oldValue, newValue, comparison)`:
 * replaces every occurrence of `oldValue`, scanning left to right without overlap.
 * Unlike `String.prototype.replace`, it replaces all occurrences, and unlike
 * `replaceAll`, `$` sequences in `newValue` are inserted literally. A `null`
 * `newValue` removes the occurrences. A case-insensitive `comparison` matches
 * `oldValue` in any case and keeps the rest of `value` as written.
 *
 * @throws ArgumentNullException when `oldValue` is `null`.
 * @throws ArgumentException when `oldValue` is empty.
 */
export function stringReplace(
  value: string,
  oldValue: string,
  newValue: string | null | undefined,
  comparison?: StringComparison,
): string {
  // Generated code is not always type-checked against this signature (a C# `null!`
  // argument reaches here), so the C# null check stays.
  if (oldValue == null) {
    throw new ArgumentNullException("oldValue");
  }

  if (oldValue.length === 0) {
    throw new ArgumentException("The value cannot be an empty string.", "oldValue");
  }

  const replacement = newValue ?? "";

  if (!ignoresCase(comparison)) {
    return value.split(oldValue).join(replacement);
  }

  const haystack = foldCase(value);
  const needle = foldCase(oldValue);
  let replaced = "";
  let copiedUpTo = 0;
  let found = haystack.indexOf(needle);

  while (found !== -1) {
    replaced += value.slice(copiedUpTo, found) + replacement;
    copiedUpTo = found + needle.length;
    found = haystack.indexOf(needle, copiedUpTo);
  }

  return replaced + value.slice(copiedUpTo);
}
