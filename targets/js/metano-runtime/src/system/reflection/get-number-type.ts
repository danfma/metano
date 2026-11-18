import { isInt32 } from "../../type-checking/index.ts";
import { DoubleType, Int32Type } from "./primitive-types.ts";
import type { PrimitiveType } from "./runtime-type.ts";

/**
 * The descriptor of a JS number: an integer in the `int` range is `Int32Type`, anything else
 * `DoubleType`. `byte`, `short`, `int` and `1.0` are indistinguishable at run time. There is
 * no `Int64Type` for a number: C# `long` is a JS `bigint` (spec 012).
 */
export function getNumberType(n: number): PrimitiveType<number> {
  return isInt32(n) ? Int32Type : DoubleType;
}
