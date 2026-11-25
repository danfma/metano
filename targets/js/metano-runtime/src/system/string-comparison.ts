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
 *   engine's default locale, `InvariantCulture` with the root locale, both at the
 *   sensitivity that ignores case (`accent`) or keeps it (`variant`).
 *
 * `null` equals only `null` and sorts before every string, as in .NET. A `Compare`
 * result has .NET's sign; .NET leaves the magnitude unspecified, so the ordinal forms
 * return the difference of the first code units that differ, or of the lengths.
 */

import { ArgumentException } from "./exceptions/argument-exception.ts";
import { ordinalUppercaseOf } from "./json/ordinal-ignore-case.ts";
import { StringComparison } from "./string-members.ts";

const UNSUPPORTED_COMPARISON_MESSAGE =
  "The string comparison type passed in is currently not supported.";

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
    default:
      return collatorFor(comparison).compare(left, right);
  }
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
