import { unboxFor } from "./unbox-for.ts";
import type { RuntimeType } from "./runtime-type.ts";

/**
 * C# `value is T`: the single entry point every type test goes through. A box of `T` is tested
 * as its raw value; a box of another type stays a box (see {@link unboxFor}).
 */
export function isType<T>(value: unknown, type: RuntimeType<T>): value is T {
  return type.test(unboxFor(value, type));
}

/**
 * C# `value as T`: the value when it is a `T`, otherwise `null` (`null` stays `null`). A box of
 * `T` answers its raw value (`(Color)o` yields the enum); a box that only passes as `object`
 * stays a box.
 */
export function asType<T>(value: unknown, type: RuntimeType<T>): T | null {
  if (value == null) {
    return null;
  }

  const candidate = unboxFor(value, type);

  return type.test(candidate) ? (candidate as T) : null;
}
