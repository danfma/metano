import { isBoxed } from "./is-boxed.ts";

/**
 * The raw value of a {@link Boxed}; anything else comes back unchanged. Shallow: a box inside
 * an array or an object is left alone.
 */
export function unbox(value: unknown): unknown {
  return isBoxed(value) ? value.value : value;
}
