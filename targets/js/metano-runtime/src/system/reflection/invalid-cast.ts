import { InvalidCastException } from "../exceptions/invalid-cast-exception.ts";
import { ownRuntimeTypeOf } from "./constructor-descriptor.ts";
import { getRuntimeType } from "./get-runtime-type.ts";
import { isBoxed } from "./is-boxed.ts";

/**
 * The `InvalidCastException` .NET throws for a cast of `value` to the type .NET names
 * `target` (`Type.ToString()`): `Unable to cast object of type 'S' to type 'T'.`, `S` named
 * as {@link describeValue} names it. Internal: shared by `castTo` and the collections'
 * `asList`, not exported from the barrel.
 */
export function invalidCast(
  value: unknown,
  target: string,
  describePrimitives = true,
): InvalidCastException {
  return new InvalidCastException(
    `Unable to cast object of type '${describeValue(value, describePrimitives)}' to type '${target}'.`,
  );
}

/**
 * The name .NET's cast message gives the value's type: the full name of a described class,
 * else the constructor's name, else `typeof`. A descriptor target also names a primitive by
 * its descriptor (`System.Int32`).
 */
function describeValue(value: unknown, describePrimitives: boolean): string {
  if (isBoxed(value)) {
    return value.type.fullName;
  }

  const described = ownRuntimeTypeOf(value);

  if (described !== undefined) {
    return described.fullName;
  }

  if (describePrimitives && (typeof value !== "object" || value === null)) {
    return getRuntimeType(value).fullName;
  }

  const constructorName = (value as { readonly constructor?: { readonly name?: string } })
    .constructor?.name;

  return typeof value === "object" && constructorName ? constructorName : typeof value;
}
