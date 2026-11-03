import type { TypeSpec } from "./types.ts";
import { JsonException } from "./errors.ts";

/**
 * The C# type name a contract reports in errors: its constructor's name, the
 * `typeName` of a polymorphic base without a constructor (an interface or a
 * `[PlainObject]` base), and `Object` for any other `[PlainObject]` contract.
 */
export function contractNameOf(spec: TypeSpec): string {
  return spec.type?.name ?? spec.polymorphism?.typeName ?? "Object";
}

/** System.Text.Json's error for a JSON value that does not fit the contract. */
export function cannotConvertTo(spec: TypeSpec): JsonException {
  return new JsonException(`The JSON value could not be converted to ${contractNameOf(spec)}.`);
}
