import { MissingMethodException } from "../exceptions/missing-method-exception.ts";
import type { RuntimeType } from "./runtime-type.ts";

/**
 * C# `new T()` (`where T : new()`): invokes `type.create()` or throws
 * `MissingMethodException` when the descriptor has no parameterless constructor.
 */
export function createInstance<T>(type: RuntimeType<T>): T {
  if (type.create === undefined) {
    throw new MissingMethodException(`Cannot create an instance of ${type.fullName}.`);
  }

  return type.create() as T;
}
