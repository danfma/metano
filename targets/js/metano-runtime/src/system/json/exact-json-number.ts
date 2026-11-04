/**
 * JSON numbers with their exact digits, as System.Text.Json reads and writes
 * a `decimal` (and, once it maps to `bigint`, a `long`, #450), instead of the
 * double a JS `number` rounds them to.
 *
 * On write, the leaf is a `JSON.rawJSON(text)` object, which `JSON.stringify`
 * writes as the text itself, unquoted (measured on Node 24.11 and Bun
 * 1.4.2). An engine without `JSON.rawJSON` writes the approximate double
 * instead, the behavior before #458. TypeScript's lib does not declare either
 * function yet, hence the local types.
 *
 * On read (#457), `JsonSerializer.deserializeText` keeps a number with more
 * significant digits than a double holds as a `JsonNumberText` leaf, which
 * the converters that read exact digits take from its text and every other
 * converter reads as the `number` `JSON.parse` gives.
 */

/** Builds the JSON leaf of a number from its exact text. */
export type ExactNumberWriter = (text: string, approximate: () => number) => unknown;

type RawJson = (text: string) => unknown;

type IsRawJson = (value: unknown) => boolean;

/** This engine's `JSON.rawJSON` / `JSON.isRawJSON`, detected once when the module loads. */
const engineJson = JSON as { rawJSON?: unknown; isRawJSON?: unknown };

const engineRawJson =
  typeof engineJson.rawJSON === "function" ? (engineJson.rawJSON as RawJson) : undefined;

const engineIsRawJson =
  typeof engineJson.isRawJSON === "function" ? (engineJson.isRawJSON as IsRawJson) : undefined;

/**
 * The writer for an engine whose `JSON.rawJSON` is `rawJson`: the raw JSON
 * leaf when it exists, `approximate()` otherwise.
 */
export function exactNumberWriterOf(rawJson: RawJson | undefined): ExactNumberWriter {
  if (rawJson === undefined) {
    return (_text, approximate) => approximate();
  }

  return (text) => rawJson(text);
}

/** The writer of this engine. */
export const writeExactNumber: ExactNumberWriter = exactNumberWriterOf(engineRawJson);

/**
 * The text of a `JSON.rawJSON` leaf, so the tree `serialize` returns can be
 * passed to `deserialize` without a `JSON.stringify` / `JSON.parse` trip;
 * `undefined` for any other value.
 */
export function rawJsonTextOf(value: unknown): string | undefined {
  const isLeaf = typeof value === "object" && engineIsRawJson?.(value) === true;

  return isLeaf ? (value as { readonly rawJSON: string }).rawJSON : undefined;
}

/**
 * A JSON number read with more significant digits than a double holds
 * exactly (#457), kept as its source text. Only the trees
 * `JsonSerializer.deserializeText` builds hold one; the converters that do not
 * read exact digits never see it (`numberOfTreeValue`).
 */
export class JsonNumberText {
  constructor(readonly text: string) {}
}

/**
 * The exact text of a number leaf: a `JsonNumberText` read from JSON text, or
 * a `JSON.rawJSON` leaf `serialize` wrote; `undefined` for any other value.
 */
export function exactNumberTextOf(value: unknown): string | undefined {
  return value instanceof JsonNumberText ? value.text : rawJsonTextOf(value);
}

/** `value`, with a `JsonNumberText` leaf read as the `number` `JSON.parse` gives. */
export function numberOfTreeValue(value: unknown): unknown {
  return value instanceof JsonNumberText ? Number(value.text) : value;
}
