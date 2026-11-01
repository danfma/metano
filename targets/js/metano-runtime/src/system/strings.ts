/**
 * Runtime helpers for `System.String` members whose C# semantics differ from the
 * closest JavaScript built-in.
 */

/**
 * The single characters .NET's `char.IsWhiteSpace` accepts besides the ranges tested in
 * {@link isDotNetWhiteSpace}. JavaScript's `trim()` and `\s` disagree with .NET on two
 * code points: they accept U+FEFF, which .NET does not, and reject U+0085, which .NET
 * accepts.
 */
const DOTNET_WHITE_SPACE_CODES: ReadonlySet<number> = new Set([
  0x0020, 0x0085, 0x00a0, 0x1680, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000,
]);

/** Whether `code` is a .NET white-space character (`char.IsWhiteSpace`). */
function isDotNetWhiteSpace(code: number): boolean {
  const isControlWhiteSpace = code >= 0x0009 && code <= 0x000d;
  const isSpaceSeparatorRange = code >= 0x2000 && code <= 0x200a;

  return isControlWhiteSpace || isSpaceSeparatorRange || DOTNET_WHITE_SPACE_CODES.has(code);
}

/**
 * C#'s `string.IsNullOrEmpty(value)`: `true` for `null`, `undefined` and `""`.
 *
 * The type predicate lets a `false` result narrow `value` to `string`, as C#'s
 * `[NotNullWhen(false)]` does.
 */
export function isNullOrEmpty(value: string | null | undefined): value is "" | null | undefined {
  return value == null || value.length === 0;
}

/**
 * What a `true` {@link isNullOrWhiteSpace} result narrows its argument to: a missing
 * value or a string the brand marks as white space only. The brand exists only in the
 * type system; it keeps the narrowed string comparable with any literal (`s === " "`).
 */
export type NullOrWhiteSpace = (string & { readonly __brand: "WhiteSpace" }) | null | undefined;

/**
 * C#'s `string.IsNullOrWhiteSpace(value)`: `true` for `null`, `undefined`, `""` and a
 * string made only of .NET white-space characters.
 *
 * The type predicate lets a `false` result narrow `value` to `string`, as C#'s
 * `[NotNullWhen(false)]` does.
 */
export function isNullOrWhiteSpace(value: string | null | undefined): value is NullOrWhiteSpace {
  if (value == null) {
    return true;
  }

  for (let index = 0; index < value.length; index++) {
    if (!isDotNetWhiteSpace(value.charCodeAt(index))) {
      return false;
    }
  }

  return true;
}

/**
 * C#'s `value.Replace(oldValue, newValue)` for the `(char, char)` and
 * `(string, string?)` overloads: replaces every ordinal occurrence of `oldValue`,
 * scanning left to right without overlap. Unlike `String.prototype.replace`, it
 * replaces all occurrences, and unlike `replaceAll`, `$` sequences in `newValue` are
 * inserted literally. A `null` `newValue` removes the occurrences.
 *
 * @throws Error with .NET's `ArgumentNullException` message when `oldValue` is `null`,
 *   and with its `ArgumentException` message when `oldValue` is empty.
 */
export function stringReplace(
  value: string,
  oldValue: string,
  newValue: string | null | undefined,
): string {
  // Generated code is not always type-checked against this signature (a C# `null!`
  // argument reaches here), so the C# null check stays.
  if (oldValue == null) {
    throw new Error("Value cannot be null. (Parameter 'oldValue')");
  }

  if (oldValue.length === 0) {
    throw new Error("The value cannot be an empty string. (Parameter 'oldValue')");
  }

  return value.split(oldValue).join(newValue ?? "");
}
