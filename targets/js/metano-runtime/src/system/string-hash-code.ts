/**
 * C#'s `value.GetHashCode(comparison)` (#769): the hash `StringComparer.Ordinal` or
 * `StringComparer.OrdinalIgnoreCase` gives, so strings equal under the comparison hash
 * alike. JavaScript has no culture sort key, so a culture-sensitive comparison throws
 * `NotSupportedException` instead of returning a hash that disagrees with
 * `string.Equals(a, b, comparison)`.
 */

import { StringComparer } from "./collections/string-comparer.ts";
import { ArgumentException } from "./exceptions/argument-exception.ts";
import { NotSupportedException } from "./exceptions/not-supported-exception.ts";
import { NullReferenceException } from "./exceptions/null-reference-exception.ts";
import { StringComparison } from "./string-members.ts";

const UNSUPPORTED_COMPARISON_MESSAGE =
  "The string comparison type passed in is currently not supported.";

const CULTURE_HASH_MESSAGE =
  "GetHashCode with a culture-sensitive StringComparison is not supported in JavaScript.";

/**
 * @throws NullReferenceException when `value` is `null`.
 * @throws ArgumentException when `comparison` is not a `StringComparison` member.
 * @throws NotSupportedException for a culture-sensitive comparison.
 */
export function stringHashCode(value: string | null, comparison: StringComparison): number {
  if (value == null) {
    throw new NullReferenceException();
  }

  switch (comparison) {
    case StringComparison.Ordinal:
      return StringComparer.ordinal.getHashCode(value);
    case StringComparison.OrdinalIgnoreCase:
      return StringComparer.ordinalIgnoreCase.getHashCode(value);
    case StringComparison.CurrentCulture:
    case StringComparison.CurrentCultureIgnoreCase:
    case StringComparison.InvariantCulture:
    case StringComparison.InvariantCultureIgnoreCase:
      throw new NotSupportedException(CULTURE_HASH_MESSAGE);
    default:
      throw new ArgumentException(UNSUPPORTED_COMPARISON_MESSAGE, "comparisonType");
  }
}
