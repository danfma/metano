import type { PrimitiveType } from "./runtime-type.ts";
import { DoubleType, Int32Type } from "./primitive-types.ts";

const INT32_MIN = -2147483648;
const INT32_MAX = 2147483647;

/**
 * The descriptor of a JS number: an integer in the `int` range is `Int32Type`, anything else
 * `DoubleType`. `byte`, `short`, `int` and `1.0` are indistinguishable at run time. There is
 * no `Int64Type` for a number: C# `long` is a JS `bigint` (spec 012).
 */
export function getNumberType(n: number): PrimitiveType<number> {
  return Number.isInteger(n) && n >= INT32_MIN && n <= INT32_MAX ? Int32Type : DoubleType;
}
