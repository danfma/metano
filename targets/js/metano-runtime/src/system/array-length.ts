/**
 * The length check of C#'s `new T[length]`: generated code builds the array as
 * `new Array<T>(arrayLength(length)).fill(default)`, so a negative length throws
 * .NET's `OverflowException` rather than JavaScript's `RangeError`.
 */
import { OverflowException } from "./exceptions/overflow-exception.ts";

/**
 * `length`, or `OverflowException` when it is negative, as .NET throws for an
 * array of a value type (`Arithmetic operation resulted in an overflow.`; .NET
 * says `Array dimensions exceeded supported range.` for a reference type).
 */
export function arrayLength(length: number): number {
  if (length < 0) {
    throw new OverflowException();
  }

  return length;
}
