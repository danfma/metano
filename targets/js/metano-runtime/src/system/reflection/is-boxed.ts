import type { Boxed } from "./box.ts";

/**
 * Whether `value` is a {@link Boxed}, by the `$boxed` marker it carries (a type-only dependency
 * on the class, so a program that never boxes does not bundle it). A marker rather than
 * `instanceof` also lets two copies of the runtime recognize each other's boxes.
 */
export function isBoxed(value: unknown): value is Boxed<unknown> {
  return (value as { readonly $boxed?: unknown } | null | undefined)?.$boxed === true;
}
