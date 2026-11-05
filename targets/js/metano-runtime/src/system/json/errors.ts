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

/**
 * Thrown for a polymorphic contract that cannot handle a payload or a value,
 * as System.Text.Json throws `System.NotSupportedException`: a payload without
 * a type discriminator for an abstract or interface base, or a runtime type
 * the base does not register. Messages carry no `Path` / `LineNumber` suffix
 * (DECIDED-5). The class is the runtime's `system/exceptions` one (spec 011
 * FR-013).
 */
export { NotSupportedException } from "../exceptions/not-supported-exception.ts";
