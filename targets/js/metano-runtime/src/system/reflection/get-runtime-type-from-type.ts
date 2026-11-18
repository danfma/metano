import { ArgumentException } from "../exceptions/argument-exception.ts";
import { ownRuntimeType } from "./constructor-descriptor.ts";
import type { RuntimeType } from "./runtime-type.ts";

/**
 * The hand-written counterpart of C# `typeof(Foo)`: a class whose own `$runtimeType` is a
 * descriptor, or a descriptor itself. Anything else is an `ArgumentException`.
 */
export function getRuntimeTypeFromType(constructorOrDescriptor: unknown): RuntimeType {
  const own = ownRuntimeType(constructorOrDescriptor);

  if (own !== undefined) {
    return own;
  }

  if (isRuntimeType(constructorOrDescriptor)) {
    return constructorOrDescriptor;
  }

  throw new ArgumentException(
    "The value is neither a class with a runtime type descriptor nor a runtime type descriptor.",
    "constructorOrDescriptor",
  );
}

function isRuntimeType(value: unknown): value is RuntimeType {
  return (
    value !== null &&
    typeof value === "object" &&
    typeof (value as { readonly kind?: unknown }).kind === "string" &&
    typeof (value as { readonly test?: unknown }).test === "function"
  );
}
