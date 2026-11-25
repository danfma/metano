/**
 * C#'s `string.Equals` and `string.Compare` with a `StringComparison` (#769), and the
 * overloads that imply one (`Compare(a, b)` and `CompareTo` are culture-sensitive,
 * `CompareOrdinal` is ordinal).
 *
 * - `Ordinal` compares UTF-16 code units.
 * - `OrdinalIgnoreCase` compares the ordinal uppercase of each code point
 *   (`ordinalUppercaseOf`: .NET's invariant simple case mapping, so `ß` never equals
 *   `SS` and `ı` never equals `I`), not a whole-string `toLowerCase()`.
 * - The culture members compare with `Intl.Collator`: `CurrentCulture` with the
 *   engine's default locale, `InvariantCulture` with the root locale, at `variant`
 *   sensitivity. The ignore-case members ignore case only, as .NET does on ICU: the
 *   strings first compare at `accent` sensitivity (which also ignores width and kana),
 *   and strings equal there are told apart by width and kana alone
 *   (`caseInsensitiveKey`), so `"Ａ"` never equals `"a"` while `ß` equals `ẞ` and
 *   `ﬀ` equals `FF`.
 *
 * The instance forms (`a.Equals(b, comparison)`, `a.Equals(b)`, `a.CompareTo(b)`) throw
 * `NullReferenceException` for a `null` receiver, as .NET does; `CompareTo(object)`
 * throws `ArgumentException` for an argument that is not a string.
 *
 * `null` equals only `null` and sorts before every string, as in .NET. A `Compare`
 * result has .NET's sign; .NET leaves the magnitude unspecified, so the ordinal forms
 * return the difference of the first code units that differ, or of the lengths.
 */

import { ArgumentException } from "./exceptions/argument-exception.ts";
import { NullReferenceException } from "./exceptions/null-reference-exception.ts";
import { ordinalUppercaseOf } from "./json/ordinal-ignore-case.ts";
import { StringComparison } from "./string-members.ts";

const UNSUPPORTED_COMPARISON_MESSAGE =
  "The string comparison type passed in is currently not supported.";

const NOT_A_STRING_MESSAGE = "Object must be of type String.";

const FIRST_SUPPLEMENTARY_CODE_POINT = 0x10000;

/* Built on first use, one per culture member, so importing the module runs no code. */
const cultureCollators: Intl.Collator[] = [];

/** The collator for a culture member (`CurrentCulture` to `InvariantCultureIgnoreCase`). */
function collatorFor(comparison: StringComparison): Intl.Collator {
  const isInvariant = comparison >= StringComparison.InvariantCulture;
  const ignoresCase = comparison % 2 === 1;

  return (cultureCollators[comparison] ??= new Intl.Collator(isInvariant ? "und" : undefined, {
    sensitivity: ignoresCase ? "accent" : "variant",
  }));
}

/** @throws ArgumentException when `comparison` is not a `StringComparison` member. */
function requireComparison(comparison: StringComparison): void {
  const isMember =
    Number.isInteger(comparison) &&
    comparison >= StringComparison.CurrentCulture &&
    comparison <= StringComparison.OrdinalIgnoreCase;

  if (!isMember) {
    throw new ArgumentException(UNSUPPORTED_COMPARISON_MESSAGE, "comparisonType");
  }
}

/** The ordinal uppercase code point of the code point at `index` in `value`. */
function foldedCodePointAt(value: string, index: number): number {
  const unit = value.charCodeAt(index);

  if (unit < 0x80) {
    return unit >= 0x61 && unit <= 0x7a ? unit - 0x20 : unit;
  }

  const codePoint = value.codePointAt(index) as number;

  return ordinalUppercaseOf(String.fromCodePoint(codePoint)).codePointAt(0) as number;
}

function compareOrdinal(left: string, right: string): number {
  const length = Math.min(left.length, right.length);

  for (let index = 0; index < length; index++) {
    const difference = left.charCodeAt(index) - right.charCodeAt(index);

    if (difference !== 0) {
      return difference;
    }
  }

  return left.length - right.length;
}

/**
 * Walks both strings code point by code point; a supplementary code point orders by its
 * value (above every BMP one), as .NET's case-insensitive comparison does.
 */
function compareOrdinalIgnoreCase(left: string, right: string): number {
  let leftIndex = 0;
  let rightIndex = 0;

  while (leftIndex < left.length && rightIndex < right.length) {
    const leftFolded = foldedCodePointAt(left, leftIndex);
    const rightFolded = foldedCodePointAt(right, rightIndex);

    if (leftFolded !== rightFolded) {
      const isSupplementary =
        leftFolded >= FIRST_SUPPLEMENTARY_CODE_POINT ||
        rightFolded >= FIRST_SUPPLEMENTARY_CODE_POINT;

      return isSupplementary ? Math.sign(leftFolded - rightFolded) : leftFolded - rightFolded;
    }

    leftIndex += (left.codePointAt(leftIndex) as number) >= FIRST_SUPPLEMENTARY_CODE_POINT ? 2 : 1;
    rightIndex +=
      (right.codePointAt(rightIndex) as number) >= FIRST_SUPPLEMENTARY_CODE_POINT ? 2 : 1;
  }

  return left.length - leftIndex - (right.length - rightIndex);
}

/** Two non-null strings under `comparison`, already checked. */
function compareWith(left: string, right: string, comparison: StringComparison): number {
  switch (comparison) {
    case StringComparison.Ordinal:
      return compareOrdinal(left, right);
    case StringComparison.OrdinalIgnoreCase:
      return compareOrdinalIgnoreCase(left, right);
    case StringComparison.CurrentCultureIgnoreCase:
    case StringComparison.InvariantCultureIgnoreCase:
      return compareCultureIgnoringCase(left, right, comparison);
    default:
      return collatorFor(comparison).compare(left, right);
  }
}

/**
 * .NET's culture `IgnoreCase` on ICU ignores case but not width (`Ａ` / `a`, `ｶ` / `カ`)
 * or kana type (`あ` / `ア`), which `accent` sensitivity ignores too. Strings that differ
 * at `accent` sensitivity keep that order; strings equal there compare their
 * case-insensitive keys at `variant` sensitivity, where only width and kana remain.
 */
function compareCultureIgnoringCase(
  left: string,
  right: string,
  comparison: StringComparison,
): number {
  const primary = collatorFor(comparison).compare(left, right);

  if (primary !== 0) {
    return primary;
  }

  const caseSensitive = (comparison - 1) as StringComparison;

  return collatorFor(caseSensitive).compare(caseInsensitiveKey(left), caseInsensitiveKey(right));
}

/**
 * `value` with every difference .NET's culture `IgnoreCase` ignores removed: each code
 * point outside the halfwidth and fullwidth forms is replaced by its compatibility
 * decomposition (`ﬀ` → `ff`, `①` → `1`) in a single case (simple uppercase, then simple
 * lowercase: `ς` and `Σ` → `σ`, `ẞ` → `ß`). Width forms stay as they are.
 */
function caseInsensitiveKey(value: string): string {
  let key = "";

  for (const character of value) {
    if (isWidthForm(character.codePointAt(0) as number)) {
      key += character;
      continue;
    }

    for (const part of character.normalize("NFKC")) {
      key += singleCase(part);
    }
  }

  return key;
}

/** U+3000 (ideographic space) and the Halfwidth and Fullwidth Forms block. */
function isWidthForm(codePoint: number): boolean {
  return codePoint === 0x3000 || (codePoint >= 0xff00 && codePoint <= 0xffef);
}

function singleCase(character: string): string {
  const upper = simpleMapping(character, character.toUpperCase());

  return simpleMapping(upper, upper.toLowerCase());
}

/** `mapped` when it is one code point (the simple mapping), else `character`. */
function simpleMapping(character: string, mapped: string): string {
  return mapped.length === character.length || [...mapped].length === 1 ? mapped : character;
}

/**
 * C#'s `string.Equals(a, b, comparison)` and `a.Equals(b, comparison)`.
 *
 * @throws ArgumentException when `comparison` is not a `StringComparison` member.
 */
export function stringEquals(
  left: string | null,
  right: string | null,
  comparison: StringComparison,
): boolean {
  requireComparison(comparison);

  if (left === right) {
    return true;
  }

  if (left === null || right === null || comparison === StringComparison.Ordinal) {
    return false;
  }

  if (comparison === StringComparison.OrdinalIgnoreCase && left.length !== right.length) {
    return false;
  }

  return compareWith(left, right, comparison) === 0;
}

/**
 * C#'s `string.Compare(a, b, comparison)` and the overloads that imply a comparison:
 * `Compare(a, b)`, `Compare(a, b, ignoreCase)`, `CompareOrdinal(a, b)`, `a.CompareTo(b)`.
 *
 * @throws ArgumentException when `comparison` is not a `StringComparison` member.
 */
export function stringCompare(
  left: string | null,
  right: string | null,
  comparison: StringComparison,
): number {
  requireComparison(comparison);

  if (left === right) {
    return 0;
  }

  if (left === null) {
    return -1;
  }

  if (right === null) {
    return 1;
  }

  return compareWith(left, right, comparison);
}

/** @throws NullReferenceException when the receiver of an instance call is `null`. */
function requireReceiver(value: string | null | undefined): string {
  if (value == null) {
    throw new NullReferenceException();
  }

  return value;
}

/**
 * C#'s `a.Equals(b, comparison)` and `a.Equals(b)` (ordinal).
 *
 * @throws NullReferenceException when `value` is `null`.
 * @throws ArgumentException when `comparison` is not a `StringComparison` member.
 */
export function stringInstanceEquals(
  value: string | null,
  other: unknown,
  comparison: StringComparison,
): boolean {
  const receiver = requireReceiver(value);

  if (other === null || other === undefined || typeof other === "string") {
    return stringEquals(receiver, other ?? null, comparison);
  }

  requireComparison(comparison);

  return false;
}

/**
 * C#'s `a.CompareTo(b)` for a `string` or `object` argument: a culture-sensitive
 * comparison; `null` sorts first.
 *
 * @throws NullReferenceException when `value` is `null`.
 * @throws ArgumentException when `other` is neither `null` nor a string.
 */
export function stringCompareTo(value: string | null, other: unknown): number {
  const receiver = requireReceiver(value);

  if (other !== null && other !== undefined && typeof other !== "string") {
    throw new ArgumentException(NOT_A_STRING_MESSAGE);
  }

  return stringCompare(receiver, other ?? null, StringComparison.CurrentCulture);
}
