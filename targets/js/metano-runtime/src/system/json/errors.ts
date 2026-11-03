/**
 * Thrown for JSON that does not match its contract, mirroring
 * `System.Text.Json.JsonException`: a value of the wrong JSON kind, a missing
 * required property, an invalid enum value, or a document nested deeper than
 * the maximum depth. Messages carry no `Path` / `LineNumber` suffix
 * (DECIDED-5).
 */
export class JsonException extends Error {
  override readonly name = "JsonException";
}
