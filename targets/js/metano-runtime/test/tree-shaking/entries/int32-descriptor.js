// One primitive descriptor: `Int32Type` with its `isInt32` check.
import { Int32Type } from "./system/reflection/primitive-types.js";

export function isInt(value) {
  return Int32Type.test(value);
}
