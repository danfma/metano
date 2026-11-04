import type { ValueConverter } from "./value-converter.ts";
import { JsonException } from "./errors.ts";

/**
 * The built-in converter of the `bytes` descriptor (`byte[]`, FR-046, slice
 * S6b): System.Text.Json's base64 wire, measured on .NET 10.
 *
 * - Writes the standard alphabet (`+`, `/`) with `=` padding and no line
 *   breaks: `[]` → `""`, `[0]` → `"AA=="`, `[1,2]` → `"AQI="`,
 *   `[255,254,253]` → `"//79"`.
 * - Reads the same text, ignoring the whitespace System.Text.Json ignores
 *   (space, tab, CR, LF) anywhere, padding included (`"AQ= ="` → `[1]`,
 *   `"  "` → `[]`). Anything else throws its conversion error: a length that
 *   is not a multiple of four once whitespace is removed (`"AQI"`), a
 *   character outside the alphabet (`"-_8="`, `"A!ID"`, form feed,
 *   no-break space, NUL), padding before the end (`"AQ==AQ=="`) or longer
 *   than two characters (`"A==="`), non-zero unused bits (`"AR=="`) and any
 *   JSON value other than a string (`[1,2]`, `1`, `true`, `{}`). JSON `null`
 *   reads as `null`.
 */
export const byteArrayConverter: ValueConverter = {
  serialize: (value: unknown) => (value == null ? null : writeBase64(value as Iterable<number>)),
  deserialize: (value: unknown) => {
    if (value == null) {
      return null;
    }

    const bytes = typeof value === "string" ? readBase64(value) : undefined;

    if (bytes === undefined) {
      throw new JsonException("The JSON value could not be converted to System.Byte[].");
    }

    return bytes;
  },
};

const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

const SEXTET_OF = new Map([...ALPHABET].map((character, sextet) => [character, sextet]));

const IGNORED_WHITESPACE = /[ \t\r\n]/g;

const PADDING = "=";

/** The base64 text of `bytes`, padded, without line breaks. */
function writeBase64(bytes: Iterable<number>): string {
  const values = Array.from(bytes, requireByte);
  const groupCount = Math.ceil(values.length / 3);

  return Array.from({ length: groupCount }, (_, group) =>
    writeGroup(values.slice(group * 3, group * 3 + 3)),
  ).join("");
}

/** The four characters of one to three bytes, padded with `=`. */
function writeGroup(group: readonly number[]): string {
  const [first = 0, second = 0, third = 0] = group;
  const bits = (first << 16) | (second << 8) | third;

  return [18, 12, 6, 0]
    .map((shift, position) => (position <= group.length ? ALPHABET[(bits >> shift) & 63] : PADDING))
    .join("");
}

function requireByte(value: number): number {
  if (!Number.isInteger(value) || value < 0 || value > 255) {
    throw new JsonException(`The value ${String(value)} is not a byte.`);
  }

  return value;
}

/** The bytes of a base64 text, or `undefined` when System.Text.Json rejects it. */
function readBase64(text: string): number[] | undefined {
  const compact = text.replace(IGNORED_WHITESPACE, "");

  if (compact.length % 4 !== 0) {
    return undefined;
  }

  const bytes: number[] = [];

  for (let index = 0; index < compact.length; index += 4) {
    const isLastQuartet = index + 4 === compact.length;
    const quartet = readQuartet(compact.slice(index, index + 4), isLastQuartet);

    if (quartet === undefined) {
      return undefined;
    }

    bytes.push(...quartet);
  }

  return bytes;
}

/**
 * The one to three bytes of four base64 characters. Only the last quartet
 * may end with one or two `=`, and the bits its padding leaves unused must be
 * zero, as .NET requires (`"AR=="` and `"AQJ="` are rejected).
 */
function readQuartet(quartet: string, isLastQuartet: boolean): number[] | undefined {
  const paddingLength = isLastQuartet ? paddingLengthOf(quartet) : 0;
  const sextets = [...quartet.slice(0, 4 - paddingLength)].map((c) => SEXTET_OF.get(c));

  if (paddingLength > 2 || sextets.some((sextet) => sextet === undefined)) {
    return undefined;
  }

  const [a = 0, b = 0, c = 0, d = 0] = sextets as number[];
  const bits = (a << 18) | (b << 12) | (c << 6) | d;
  const byteCount = 3 - paddingLength;
  const unusedBits = bits & ((1 << (8 * paddingLength)) - 1);

  if (unusedBits !== 0) {
    return undefined;
  }

  return [bits >> 16, (bits >> 8) & 255, bits & 255].slice(0, byteCount);
}

function paddingLengthOf(quartet: string): number {
  return quartet.length - quartet.replace(/=+$/, "").length;
}
