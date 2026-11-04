/**
 * Writes a JSON number with its exact digits, as System.Text.Json writes a
 * `decimal` (and, once it maps to `bigint`, a `long`, #450), instead of the
 * double a JS `number` would round it to.
 *
 * The leaf is a `JSON.rawJSON(text)` object, which `JSON.stringify` writes
 * as the text itself, unquoted (measured on Node 24.11 and Bun 1.4.2). An
 * engine without `JSON.rawJSON` writes the approximate double instead, the
 * behavior before #458. TypeScript's lib does not declare either function
 * yet, hence the local types.
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
