/**
 * Writes the tree `JsonSerializer.serialize` returns as the JSON text
 * System.Text.Json's `JsonSerializer.Serialize(value, JsonTypeInfo<T>)`
 * writes with its default options (#741, measured on .NET 10):
 *
 * - no whitespace, unless the context writes indented text (#752): `writeIndented` puts each
 *   array element and each property on its own line, indented by `indentSize` copies of
 *   `indentCharacter` per level (a space and 2 by default), with `newLine` (`"\n"` by default)
 *   between lines, `": "` after a property name, and `[]` / `{}` for an empty container;
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
import type { JsonWriteOptions } from "./types.ts";

/** The message STJ throws for a NaN or infinite number written as JSON. */
const NON_FINITE_NUMBER_MESSAGE =
  ".NET number values such as positive and negative infinity cannot be written as valid JSON. " +
  "To make it work when using 'JsonSerializer', consider specifying " +
  "'JsonNumberHandling.AllowNamedFloatingPointLiterals' " +
  "(see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).";

/** How the writer lays out containers: the compact text, or one line per element. */
interface Layout {
  /** The text before each element of a container at `depth`, or `""` when compact. */
  lineStart(depth: number): string;
  /** The text between the last element of a container and its closing bracket. */
  lineEnd(depth: number): string;
  /** The text after a property name's colon. */
  readonly afterColon: string;
}

const COMPACT: Layout = {
  lineStart: () => "",
  lineEnd: () => "",
  afterColon: "",
};

function indentedLayout(options: JsonWriteOptions): Layout {
  const unit = (options.indentCharacter ?? " ").repeat(options.indentSize ?? 2);
  const newLine = options.newLine ?? "\n";

  return {
    lineStart: (depth) => newLine + unit.repeat(depth),
    lineEnd: (depth) => newLine + unit.repeat(depth - 1),
    afterColon: " ",
  };
}

/**
 * The JSON text of `tree`, as System.Text.Json writes it: compact, or laid out as
 * `options` say (the context's `WriteIndented` family, #752).
 */
export function writeJsonText(tree: unknown, options?: JsonWriteOptions): string {
  const parts: string[] = [];
  const layout = options?.writeIndented ? indentedLayout(options) : COMPACT;

  writeValue(tree, parts, layout, 0);

  return parts.join("");
}

function writeValue(value: unknown, parts: string[], layout: Layout, depth: number): void {
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

  writeObject(value as object, parts, layout, depth);
}

function writeObject(value: object, parts: string[], layout: Layout, depth: number): void {
  const rawText = rawJsonTextOf(value);

  if (rawText !== undefined) {
    parts.push(rawText);

    return;
  }

  const toJson = (value as { toJSON?: unknown }).toJSON;

  if (typeof toJson === "function") {
    writeValue(toJson.call(value), parts, layout, depth);

    return;
  }

  if (Array.isArray(value)) {
    writeArray(value, parts, layout, depth + 1);

    return;
  }

  writeProperties(value as Record<string, unknown>, parts, layout, depth + 1);
}

/** `depth` is the nesting level of the array's elements. */
function writeArray(
  items: readonly unknown[],
  parts: string[],
  layout: Layout,
  depth: number,
): void {
  parts.push("[");

  items.forEach((item, index) => {
    if (index > 0) {
      parts.push(",");
    }

    parts.push(layout.lineStart(depth));
    writeValue(item, parts, layout, depth);
  });

  if (items.length > 0) {
    parts.push(layout.lineEnd(depth));
  }

  parts.push("]");
}

/**
 * An object's own enumerable properties; an `undefined` one is skipped, as `JSON.stringify`
 * skips it. `depth` is the nesting level of the properties.
 */
function writeProperties(
  object: Record<string, unknown>,
  parts: string[],
  layout: Layout,
  depth: number,
): void {
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
    parts.push(layout.lineStart(depth), quoted(key), ":", layout.afterColon);
    writeValue(member, parts, layout, depth);
  }

  if (!first) {
    parts.push(layout.lineEnd(depth));
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
