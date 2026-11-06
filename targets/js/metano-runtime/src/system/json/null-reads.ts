import type { JsonTypeInfo, TypeDescriptor } from "./types.ts";
import { cannotConvertTo } from "./contract-name.ts";

/**
 * JSON `null` read through a spec, as System.Text.Json reads it (FR-037,
 * measured on .NET 10): `null` for a reference type (a class or record
 * contract, polymorphic and interface bases included, `[PlainObject]`
 * types, collections, dictionaries, sets, `string`) and for `Nullable<T>`;
 * a `JsonException` naming the type for a value type, which its spec marks
 * with `valueType`. At the root the check runs before any converter, custom
 * ones included; a `ref` to a struct contract runs it for a property, an
 * element or a dictionary value.
 */
export function readNull(spec: JsonTypeInfo): null {
  if (!spec.valueType) {
    return null;
  }

  throw cannotConvertTo(spec);
}

/**
 * The CLR name a non-nullable value-type descriptor reports in its
 * `JsonException`, or `undefined` when the descriptor reads `null` (#471,
 * measured on .NET 10): a `string` primitive, containers, `byte[]`, a
 * `nullable` and a `ref` read `null`; a numeric or `bool`/`char` primitive,
 * an enum, a branded value, a temporal value and a `decimal` reject it,
 * naming the type the descriptor carries (the element type, wherever the
 * value sits; the declaring contract names itself at the property).
 */
export function valueTypeNameOf(descriptor: TypeDescriptor): string | undefined {
  switch (descriptor.kind) {
    case "primitive":
      return descriptor.typeName ?? descriptor.numberType;
    case "int64":
      return descriptor.typeName ?? descriptor.numberType;
    case "decimal":
      return descriptor.typeName ?? "System.Decimal";
    case "temporal":
    case "enum":
    case "numericEnum":
    case "branded":
      return descriptor.typeName;
    default:
      return undefined;
  }
}
