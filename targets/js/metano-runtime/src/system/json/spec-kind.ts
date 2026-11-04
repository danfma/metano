import type { JsonTypeInfo, ValueSpec } from "./types.ts";

/** A `ValueSpec` carries a `descriptor`; a `TypeSpec` never does. */
export function isValueSpec<T>(spec: JsonTypeInfo<T>): spec is ValueSpec<T> {
  return "descriptor" in spec;
}
