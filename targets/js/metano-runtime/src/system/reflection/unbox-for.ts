import { isBoxed } from "./is-boxed.ts";
import type { RuntimeType } from "./runtime-type.ts";

/**
 * What a type test of `type` runs on: the raw value when `value` is a box of that very type
 * (compared by `fullName`, so two descriptor objects of one type agree), else `value` itself.
 * A box of another type therefore stays a box, which the enum and primitive tests reject and
 * the `object` / `unknown` tests accept, as in C# (`(object)Color.Red is Perms` is `false`).
 * The unwrap happens once, here; descriptors never see a `Boxed` of their own type.
 */
export function unboxFor(value: unknown, type: RuntimeType): unknown {
  if (!isBoxed(value)) {
    return value;
  }

  const target = type.kind === "nullable" ? type.inner() : type;

  return value.type.fullName === target.fullName ? value.value : value;
}
