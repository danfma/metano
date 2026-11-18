import type { RuntimeType } from "./runtime-type.ts";

/** C# `value is T`: the single entry point every type test goes through. */
export function isType<T>(value: unknown, type: RuntimeType<T>): value is T {
  return type.test(value);
}

/** C# `value as T`: the value when it is a `T`, otherwise `null` (`null` stays `null`). */
export function asType<T>(value: unknown, type: RuntimeType<T>): T | null {
  return value != null && isType(value, type) ? value : null;
}
