/**
 * C#'s `IComparable<T>.CompareTo` / `IComparable.CompareTo` called through the interface
 * or a type parameter (`((IComparable<string>)s).CompareTo(t)`, `a.CompareTo(b)` with
 * `T : IComparable<T>`): the receiver may be a string or a number, which have no
 * `compareTo` method in JavaScript (#769). A string compares culture-sensitively, as
 * `string.CompareTo` does; any other value goes through `Comparer<T>.Default`'s order
 * (`defaultCompare`), which calls the receiver's own `compareTo` for an object.
 */

import { defaultCompare } from "./collections/default-comparer.ts";
import { NullReferenceException } from "./exceptions/null-reference-exception.ts";
import { stringCompareTo } from "./string-comparison.ts";

/** @throws NullReferenceException when `value` is `null`. */
export function comparableCompareTo(value: unknown, other: unknown): number {
  if (value === null || value === undefined) {
    throw new NullReferenceException();
  }

  return typeof value === "string" ? stringCompareTo(value, other) : defaultCompare(value, other);
}
