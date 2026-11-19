import { isBoxed } from "./is-boxed.ts";

/**
 * .NET's `Equals` on two boxes (spec 011 FR-051): both are boxes of the same type, compared by
 * `fullName` rather than descriptor identity, and their raw values are equal. A box never equals
 * a raw value, and the rule is symmetric and transitive.
 */
export function boxesEqual(a: unknown, b: unknown): boolean {
  return isBoxed(a) && isBoxed(b) && a.type.fullName === b.type.fullName && a.value === b.value;
}
