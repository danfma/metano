import type { Boxed } from "./box.ts";

/**
 * Whether `value` is a {@link Boxed}, by the `$boxed` marker it carries (it also needs an object `type`, so a JSON object carrying `$boxed: true` is not taken for one; a type-only dependency
 * on the class, so a program that never boxes does not bundle it). A marker rather than
 * `instanceof` also lets two copies of the runtime recognize each other's boxes.
 */
export function isBoxed(value: unknown): value is Boxed<unknown> {
  const candidate = value as
    | { readonly $boxed?: unknown; readonly type?: unknown }
    | null
    | undefined;

  return (
    candidate?.$boxed === true && typeof candidate.type === "object" && candidate.type !== null
  );
}
