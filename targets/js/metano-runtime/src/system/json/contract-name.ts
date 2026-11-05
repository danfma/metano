import type { JsonTypeInfo, TypeSpec } from "./types.ts";
import { JsonException } from "./errors.ts";
import { isValueSpec } from "./spec-kind.ts";

/**
 * The C# type name a contract reports in errors: its constructor's name, the
 * `typeName` of a polymorphic base without a constructor (an interface or a
 * `[PlainObject]` base), and `Object` for any other `[PlainObject]` contract.
 */
export function contractNameOf(spec: TypeSpec): string {
  return spec.type?.name ?? spec.polymorphism?.typeName ?? "Object";
}

/**
 * The type a conversion error names: the CLR name a value-type spec carries
 * (`typeName`, as System.Text.Json prints it: `MyApp.SPoint`,
 * `System.Int32`), else the contract's name (`contractNameOf`), and
 * `Object` for a value spec without one (a hand-written spec).
 */
function convertedTypeNameOf(spec: JsonTypeInfo): string {
  if (spec.typeName !== undefined) {
    return spec.typeName;
  }

  return isValueSpec(spec) ? "Object" : contractNameOf(spec);
}

/** System.Text.Json's error for a JSON value that does not fit the spec. */
export function cannotConvertTo(spec: JsonTypeInfo): JsonException {
  return cannotConvertToTypeName(convertedTypeNameOf(spec));
}

/**
 * System.Text.Json's error for a JSON value that does not convert to the CLR
 * type `typeName` (a dictionary key, #479); `Object` without one.
 */
export function cannotConvertToTypeName(typeName: string | undefined): JsonException {
  return new JsonException(`The JSON value could not be converted to ${typeName ?? "Object"}.`);
}
