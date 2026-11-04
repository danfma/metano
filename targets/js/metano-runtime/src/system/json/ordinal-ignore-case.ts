/**
 * `StringComparer.OrdinalIgnoreCase`, which System.Text.Json uses to match
 * JSON keys to properties under `PropertyNameCaseInsensitive` (FR-048): two
 * names are equal when every code point has the same simple uppercase
 * mapping. `toUpperCase()` alone applies the full mapping instead, so it is
 * kept only where it yields one code point (`ß` stays `ß`, never `SS`), and
 * .NET's exceptions are listed explicitly.
 *
 * Compared against .NET 10 over every BMP character: the rule differs only
 * where the JS engine's Unicode version differs from .NET's (Node 24:
 * U+A7CF, U+A7D3, U+A7D5).
 */

/**
 * Code points whose ordinal uppercase is not `toUpperCase()` (measured): `ı`
 * and `ſ` never match ASCII `I` / `S`, and the Greek letters with
 * ypogegrammeni map to their titlecase (simple uppercase) form, which the
 * full mapping splits into two characters.
 */
const ORDINAL_UPPERCASE_EXCEPTIONS: ReadonlyMap<string, string> = new Map([
  ["ı", "ı"],
  ["ſ", "ſ"],
  ...ypogegrammeniPairs(0x1f80, 0x1f87, 0x1f88),
  ...ypogegrammeniPairs(0x1f90, 0x1f97, 0x1f98),
  ...ypogegrammeniPairs(0x1fa0, 0x1fa7, 0x1fa8),
  ...ypogegrammeniPairs(0x1fb3, 0x1fb3, 0x1fbc),
  ...ypogegrammeniPairs(0x1fc3, 0x1fc3, 0x1fcc),
  ...ypogegrammeniPairs(0x1ff3, 0x1ff3, 0x1ffc),
]);

/** The key under which `name` equals every name that matches it ignoring case. */
export function ordinalIgnoreCaseKeyOf(name: string): string {
  return Array.from(name, ordinalUppercaseOf).join("");
}

function ordinalUppercaseOf(codePoint: string): string {
  const exception = ORDINAL_UPPERCASE_EXCEPTIONS.get(codePoint);

  if (exception !== undefined) {
    return exception;
  }

  const uppercase = codePoint.toUpperCase();

  return [...uppercase].length === 1 ? uppercase : codePoint;
}

function ypogegrammeniPairs(
  first: number,
  last: number,
  firstUppercase: number,
): [string, string][] {
  return Array.from({ length: last - first + 1 }, (_, offset) => [
    String.fromCodePoint(first + offset),
    String.fromCodePoint(firstUppercase + offset),
  ]);
}
