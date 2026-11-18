import type { EnumType } from "./runtime-type.ts";

/**
 * `Enum.IsDefined`: whether `value` is one of the declared members. A string is checked
 * against the member names, as .NET does (a `[StringEnum]` also accepts its values); anything
 * else against the declared values, so a combined `[Flags]` value is not defined.
 */
export function enumIsDefined<E>(type: EnumType<E>, value: unknown): boolean {
  if (typeof value === "string") {
    return type.names.includes(value) || (type.kind === "stringEnum" && isDeclared(type, value));
  }

  return isDeclared(type, value);
}

function isDeclared<E>(type: EnumType<E>, value: unknown): boolean {
  return (type.values as ReadonlyArray<unknown>).includes(value);
}
