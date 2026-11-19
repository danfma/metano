import { isBoxed } from "./is-boxed.ts";
import type { RuntimeType } from "./runtime-type.ts";
import { unboxFor } from "./unbox-for.ts";

/**
 * What an explicit unboxing cast `(T)o` runs its test on. The CLR lets a numeric enum box unbox
 * to its underlying integral type and to any other numeric enum with the same underlying type
 * (`(int)(object)Color.Green`, `(Perms)(object)Color.Green`), and throws when the underlying
 * type differs (`(long)` of an Int32 enum box). Everything else follows {@link unboxFor}, and
 * so does a nullable target (`(int?)o` throws on an enum box). `is` and `as` stay strict: only
 * the cast is lenient, so `isType` and `asType` never come here.
 */
export function unboxForCast(value: unknown, type: RuntimeType): unknown {
  return isBoxed(value) &&
    underlyingName(value.type) === underlyingName(type) &&
    underlyingName(value.type) !== undefined
    ? value.value
    : unboxFor(value, type);
}

/** The integral type a numeric enum is stored as, or the primitive itself; `undefined` for any other descriptor. */
function underlyingName(type: RuntimeType): string | undefined {
  switch (type.kind) {
    case "enum":
      return type.underlying ?? "Int32";
    case "primitive":
      return type.primitive;
    default:
      return undefined;
  }
}
