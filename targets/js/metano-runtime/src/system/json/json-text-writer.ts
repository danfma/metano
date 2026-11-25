/**
 * Writes the tree `JsonSerializer.serialize` returns as the JSON text
 * System.Text.Json's `JsonSerializer.Serialize(value, JsonTypeInfo<T>)`
 * writes with its default options (#741, measured on .NET 10):
 *
 * - no whitespace;
 * - strings escaped by `JavaScriptEncoder.Default`: `\\`, `\b`, `\f`, `\n`,
 *   `\r` and `\t` keep their short escapes; every other control character,
 *   `"`, `&`, `'`, `+`, `<`, `>`, `` ` `` and every character outside
 *   printable ASCII is written `\uXXXX` (upper-case hex, one escape per
 *   UTF-16 code unit), and a lone surrogate is written `\uFFFD`;
 * - numbers in .NET's shortest round-trip text (`1E+20`, `1E-07`, `-0`),
 *   a `JSON.rawJSON` leaf (an exact `decimal` / 64-bit integer) as its text;
 *   NaN and the infinities throw `ArgumentException`, as STJ throws for them.
 */

import { formatDouble } from "../double-format.ts";
import { ArgumentException } from "../exceptions/argument-exception.ts";
import { rawJsonTextOf } from "./exact-json-number.ts";

/** The message STJ throws for a NaN or infinite number written as JSON. */
const NON_FINITE_NUMBER_MESSAGE =
  ".NET number values such as positive and negative infinity cannot be written as valid JSON. " +
  "To make it work when using 'JsonSerializer', consider specifying " +
  "'JsonNumberHandling.AllowNamedFloatingPointLiterals' " +
  "(see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).";

/** The JSON text of `tree`, as System.Text.Json writes it. */
export function writeJsonText(tree: unknown): string {
  const parts: string[] = [];

  writeValue(tree, parts);

  return parts.join("");
}

function writeValue(value: unknown, parts: string[]): void {
  if (value == null) {
    parts.push("null");

    return;
  }

  switch (typeof value) {
    case "string":
      parts.push(quoted(value));

      return;
    case "number":
      parts.push(numberText(value));

      return;
    case "bigint":
    case "boolean":
      parts.push(String(value));

      return;
  }

  writeObject(value as object, parts);
}

function writeObject(value: object, parts: string[]): void {
  const rawText = rawJsonTextOf(value);

  if (rawText !== undefined) {
    parts.push(rawText);

    return;
  }

  const toJson = (value as { toJSON?: unknown }).toJSON;

  if (typeof toJson === "function") {
    writeValue(toJson.call(value), parts);

    return;
  }

  if (Array.isArray(value)) {
    writeArray(value, parts);

    return;
  }

  writeProperties(value as Record<string, unknown>, parts);
}

function writeArray(items: readonly unknown[], parts: string[]): void {
  parts.push("[");

  items.forEach((item, index) => {
    if (index > 0) {
      parts.push(",");
    }

    writeValue(item, parts);
  });

  parts.push("]");
}

/** An object's own enumerable properties; an `undefined` one is skipped, as `JSON.stringify` skips it. */
function writeProperties(object: Record<string, unknown>, parts: string[]): void {
  let first = true;

  parts.push("{");

  for (const key of Object.keys(object)) {
    const member = object[key];

    if (member === undefined) {
      continue;
    }

    if (!first) {
      parts.push(",");
    }

    first = false;
    parts.push(quoted(key), ":");
    writeValue(member, parts);
  }

  parts.push("}");
}

function numberText(value: number): string {
  if (!Number.isFinite(value)) {
    throw new ArgumentException(NON_FINITE_NUMBER_MESSAGE);
  }

  return formatDouble(value);
}

function quoted(text: string): string {
  for (let index = 0; index < text.length; index++) {
    if (isEscaped(text.charCodeAt(index))) {
      return `"${escaped(text)}"`;
    }
  }

  return `"${text}"`;
}

/** Whether `JavaScriptEncoder.Default` escapes the UTF-16 code unit `code`. */
function isEscaped(code: number): boolean {
  switch (code) {
    case 0x22: // "
    case 0x26: // &
    case 0x27: // '
    case 0x2b: // +
    case 0x3c: // <
    case 0x3e: // >
    case 0x5c: // backslash
    case 0x60: // `
      return true;
    default:
      return code < 0x20 || code >= 0x7f;
  }
}

/** The short escape STJ keeps for `code`, or `undefined`. */
function shortEscapeOf(code: number): string | undefined {
  switch (code) {
    case 0x08:
      return "\\b";
    case 0x09:
      return "\\t";
    case 0x0a:
      return "\\n";
    case 0x0c:
      return "\\f";
    case 0x0d:
      return "\\r";
    case 0x5c:
      return "\\\\";
    default:
      return undefined;
  }
}

function escaped(text: string): string {
  let result = "";

  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index);

    if (isHighSurrogate(code) && isLowSurrogate(text.charCodeAt(index + 1))) {
      result += unicodeEscape(code) + unicodeEscape(text.charCodeAt(index + 1));
      index++;
    } else {
      result += escapedCodeUnit(code);
    }
  }

  return result;
}

function escapedCodeUnit(code: number): string {
  const shortEscape = shortEscapeOf(code);

  if (shortEscape !== undefined) {
    return shortEscape;
  }

  if (isHighSurrogate(code) || isLowSurrogate(code)) {
    return unicodeEscape(0xfffd);
  }

  return isEscaped(code) ? unicodeEscape(code) : String.fromCharCode(code);
}

function unicodeEscape(code: number): string {
  return `\\u${code.toString(16).toUpperCase().padStart(4, "0")}`;
}

function isHighSurrogate(code: number): boolean {
  return code >= 0xd800 && code <= 0xdbff;
}

function isLowSurrogate(code: number): boolean {
  return code >= 0xdc00 && code <= 0xdfff;
}
