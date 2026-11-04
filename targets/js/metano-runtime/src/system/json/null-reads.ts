import type { JsonTypeInfo } from "./types.ts";
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
