import type { JsonTypeInfo, TypeDescriptor, ValueSpec } from "./types.ts";
import { cannotConvertTo } from "./contract-name.ts";
import { JsonException } from "./errors.ts";

/**
 * JSON `null` at a root, as System.Text.Json reads it (FR-037, measured on
 * .NET 10): `null` for a reference type (a class or record contract,
 * polymorphic and interface bases included, `[PlainObject]` types,
 * collections, dictionaries, sets, `string`) and for `Nullable<T>`; a
 * `JsonException` for a value type, which its spec marks with `valueType`.
 * The check runs before any converter, custom ones included.
 */
export function readNullRoot(spec: JsonTypeInfo): null {
  if (!spec.valueType) {
    return null;
  }

  throw "descriptor" in spec ? cannotConvertValueRoot(spec) : cannotConvertTo(spec);
}

/**
 * System.Text.Json names the root's CLR type. A value spec carries only its
 * descriptor, which names it for a number of a context that reads numbers
 * from strings (`numberType`) and for a `decimal`; any other value type is
 * named generically.
 */
function cannotConvertValueRoot(spec: ValueSpec): JsonException {
  const typeName = clrNameOf(spec.descriptor) ?? "a value type";

  return new JsonException(`The JSON value could not be converted to ${typeName}.`);
}

function clrNameOf(descriptor: TypeDescriptor): string | undefined {
  switch (descriptor.kind) {
    case "primitive":
      return descriptor.numberType;
    case "decimal":
      return "System.Decimal";
    default:
      return undefined;
  }
}
