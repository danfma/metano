import { isBoxed } from "./is-boxed.ts";
import type { RuntimeType } from "./runtime-type.ts";

/** Whether `value` is a box of exactly `type` (by `fullName`); a raw value never is. */
export function isBoxOf(value: unknown, type: RuntimeType): boolean {
  return isBoxed(value) && value.type.fullName === type.fullName;
}
