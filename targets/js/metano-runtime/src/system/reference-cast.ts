import { InvalidCastException } from "./exceptions/invalid-cast-exception.ts";

/** A class value, abstract or not, that `instanceof` can test. */
type RuntimeClass = abstract new (...args: never[]) => unknown;

/**
 * Mirrors a C# explicit reference conversion `(T)value`: `null` passes, as C# lets a null
 * reference through, and a value that is not an instance of `type` throws the
 * `InvalidCastException` .NET throws. The compiler passes the target type as the type
 * argument, so the result is typed; a cast to a type with no runtime class (an interface, a
 * type parameter) is only a TypeScript assertion and does not come here.
 */
export function castTo<T>(value: unknown, type: RuntimeClass): T {
  if (value == null || value instanceof type) {
    return value as T;
  }

  throw new InvalidCastException(
    `Unable to cast object of type '${runtimeTypeName(value)}' to type '${type.name}'.`,
  );
}

/** The name .NET's cast message gives the value's type. */
function runtimeTypeName(value: unknown): string {
  return value !== null && typeof value === "object" && value.constructor.name !== ""
    ? value.constructor.name
    : typeof value;
}
