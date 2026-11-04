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

type CasePair = readonly [character: string, uppercase: string];

/**
 * The pairs of the code points `first..last`, mapped one to one onto the
 * code points from `firstUppercase` on.
 */
function rangePairs(first: number, last: number, firstUppercase: number): ReadonlyArray<CasePair> {
  return Array.from({ length: last - first + 1 }, (_, offset) => [
    String.fromCodePoint(first + offset),
    String.fromCodePoint(firstUppercase + offset),
  ]);
}

/**
 * Code points whose ordinal uppercase is not `toUpperCase()` (measured): `ı`
 * and `ſ` never match ASCII `I` / `S`, and the Greek letters with
 * ypogegrammeni (iota subscript) map to their titlecase form with
 * prosgegrammeni, which is their simple uppercase; the full mapping splits
 * them into a capital and `Ι`.
 */
const ORDINAL_UPPERCASE_EXCEPTIONS: ReadonlyMap<string, string> = new Map<string, string>([
  ["ı", "ı"],
  ["ſ", "ſ"],
  // ᾀ–ᾇ (alpha with psili / dasia and accents) → ᾈ–ᾏ
  ...rangePairs(0x1f80, 0x1f87, 0x1f88),
  // ᾐ–ᾗ (eta with psili / dasia and accents) → ᾘ–ᾟ
  ...rangePairs(0x1f90, 0x1f97, 0x1f98),
  // ᾠ–ᾧ (omega with psili / dasia and accents) → ᾨ–ᾯ
  ...rangePairs(0x1fa0, 0x1fa7, 0x1fa8),
  // ᾳ, ῃ, ῳ (alpha, eta, omega with ypogegrammeni) → ᾼ, ῌ, ῼ
  ...rangePairs(0x1fb3, 0x1fb3, 0x1fbc),
  ...rangePairs(0x1fc3, 0x1fc3, 0x1fcc),
  ...rangePairs(0x1ff3, 0x1ff3, 0x1ffc),
]);

/** The key under which `name` equals every name that matches it ignoring case. */
export function ordinalIgnoreCaseKeyOf(name: string): string {
  return Array.from(name, ordinalUppercaseOf).join("");
}

/** The ordinal uppercase of one code point (`character` may be a surrogate pair). */
function ordinalUppercaseOf(character: string): string {
  const exception = ORDINAL_UPPERCASE_EXCEPTIONS.get(character);

  if (exception !== undefined) {
    return exception;
  }

  const uppercase = character.toUpperCase();

  return isSingleCodePoint(uppercase) ? uppercase : character;
}

function isSingleCodePoint(text: string): boolean {
  return [...text].length === 1;
}
