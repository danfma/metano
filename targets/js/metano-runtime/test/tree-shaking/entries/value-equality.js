// Value equality in a program with no boxing conversion: record `==`, `object.Equals`, the
// default comparer and `GetHashCode()` (spec 011 FR-051).
import { defaultEquals, equals, hashCode, objectEquals } from "./system/equality.js";

export function compare(a, b) {
  return [equals(a, b), defaultEquals(a, b), objectEquals(a, b), hashCode(a)];
}
