import { InvalidCastException } from "../exceptions/invalid-cast-exception.ts";
import { ownRuntimeType, ownRuntimeTypeOf } from "./constructor-descriptor.ts";
import { getRuntimeType } from "./get-runtime-type.ts";
import { isBoxed } from "./is-boxed.ts";
import type { RuntimeType } from "./runtime-type.ts";
import { unboxForCast } from "./unbox-for-cast.ts";

/** A class value, abstract or not, that `instanceof` can test. */
type RuntimeClass = abstract new (...args: never[]) => unknown;

/**
 * Mirrors a C# explicit reference conversion `(T)value`: `null` passes, as C# lets a null
 * reference through, and a value that is not a `T` throws the `InvalidCastException` .NET
 * throws. The target is a runtime type descriptor (tested by `isType`) or a class (tested by
 * `instanceof`); the compiler passes the target type as the type argument, so the result is
 * typed. A box of the target type is unwrapped once, so `(Color)o` yields the raw enum; an
 * enum box also unboxes to its underlying integral type and to another enum with the same
 * underlying type, as the CLR does (see {@link unboxForCast}). A cast to a type with neither
 * (a type parameter) is only a TypeScript assertion and does not come here.
 */
export function castTo<T>(value: unknown, type: RuntimeType<T> | RuntimeClass): T {
  if (value == null) {
    return value as T;
  }

  if (typeof type === "function") {
    return castToClass(value, type);
  }

  const candidate = unboxForCast(value, type);

  if (type.test(candidate)) {
    return candidate as T;
  }

  throw invalidCast(describeValue(value, true), type.fullName);
}

function castToClass<T>(value: unknown, type: RuntimeClass): T {
  if (value instanceof type) {
    return value as T;
  }

  throw invalidCast(describeValue(value, false), ownRuntimeType(type)?.fullName ?? type.name);
}

function invalidCast(source: string, target: string): InvalidCastException {
  return new InvalidCastException(`Unable to cast object of type '${source}' to type '${target}'.`);
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
