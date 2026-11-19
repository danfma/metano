import { ownRuntimeType } from "./constructor-descriptor.ts";
import { invalidCast } from "./invalid-cast.ts";
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

  throw invalidCast(value, type.fullName);
}

function castToClass<T>(value: unknown, type: RuntimeClass): T {
  if (value instanceof type) {
    return value as T;
  }

  throw invalidCast(value, ownRuntimeType(type)?.fullName ?? type.name, false);
}
