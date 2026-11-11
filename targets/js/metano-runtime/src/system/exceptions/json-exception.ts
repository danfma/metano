import { Exception } from "./exception.ts";

/** The message `new System.Text.Json.JsonException()` carries on .NET. */
const JSON_EXCEPTION_MESSAGE = "Exception of type 'System.Text.Json.JsonException' was thrown.";

/**
 * Mirrors `System.Text.Json.JsonException`: JSON that does not match its
 * contract — a value of the wrong JSON kind, a missing required property, an
 * invalid enum value, or a document nested deeper than the maximum depth.
 * Messages carry no `Path` / `LineNumber` suffix (spec 009 DECIDED-5).
 */
export class JsonException extends Exception {
  override readonly name: string = "JsonException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? JSON_EXCEPTION_MESSAGE, innerException);
  }

  protected override get typeNamespace(): string {
    return "System.Text.Json";
  }
}
