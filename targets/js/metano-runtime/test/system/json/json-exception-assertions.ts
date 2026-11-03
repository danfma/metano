import { expect } from "bun:test";
import { JsonException, NotSupportedException } from "#/system/json/index.ts";

/** Runs `action` and returns what it throws; fails when it returns. */
export function errorOf(action: () => unknown): unknown {
  try {
    action();
  } catch (error) {
    return error;
  }

  throw new Error("Expected the action to throw.");
}

/** Asserts that `action` throws a `JsonException` with exactly `message`. */
export function expectJsonException(action: () => unknown, message: string): void {
  const error = errorOf(action);

  expect(error).toBeInstanceOf(JsonException);
  expect((error as JsonException).name).toBe("JsonException");
  expect((error as JsonException).message).toBe(message);
}

/** Asserts that `action` throws a `NotSupportedException` with exactly `message`. */
export function expectNotSupportedException(action: () => unknown, message: string): void {
  const error = errorOf(action);

  expect(error).toBeInstanceOf(NotSupportedException);
  expect((error as NotSupportedException).name).toBe("NotSupportedException");
  expect((error as NotSupportedException).message).toBe(message);
}

// ─── System.Text.Json messages (without the Path / LineNumber suffix) ────────

/** A polymorphic abstract or interface base read without a usable tag. */
export function missingDiscriminatorMessage(baseName: string): string {
  return (
    `The JSON payload for polymorphic interface or abstract type '${baseName}' ` +
    "must specify a type discriminator."
  );
}

/** A runtime type the polymorphic base does not register, under `failSerialization`. */
export function unsupportedRuntimeTypeMessage(runtimeName: string, baseName: string): string {
  return `Runtime type '${runtimeName}' is not supported by polymorphic type '${baseName}'.`;
}

/** A tag no registration declares. */
export function unrecognizedDiscriminatorMessage(id: string): string {
  return `Read unrecognized type discriminator id '${id}'.`;
}

/** A tag that is neither a JSON string nor a JSON number. */
export function metadataTokenMessage(token: string): string {
  return (
    "The '$id', '$ref' or '$type' metadata properties must be JSON strings. " +
    `Current token type is '${token}'.`
  );
}

/** A JSON value that does not fit the contract. */
export function cannotConvertMessage(typeName: string): string {
  return `The JSON value could not be converted to ${typeName}.`;
}

/** A discriminator, `$id` or `$ref` key that is not the first key of a polymorphic payload. */
export const METADATA_NOT_FIRST_MESSAGE =
  "The metadata property is either not supported by the type or is not the first property in " +
  "the deserialized JSON object.";

/** Any other `$`-prefixed key in a polymorphic payload. */
export const DOLLAR_PROPERTY_MESSAGE =
  "Properties that start with '$' are not allowed in types that support metadata. Either " +
  "replace the character or disable reference preservation and polymorphic deserialization.";
