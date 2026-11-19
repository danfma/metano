import type { Comparison } from "./default-comparer.ts";

/**
 * The sorted-array search that `SortedList` uses (spec 011 FR-054). Internal:
 * not re-exported by the `system/collections` barrel. The comparer is
 * injected and the functions throw nothing of their own.
 */

/**
 * .NET's `Array.BinarySearch(array, index, length, value, comparer)` over
 * `array[start .. start + length)`: the index of an element that compares
 * equal to `value`, or the bitwise complement of the index where `value`
 * would be inserted. The comparer is called as `compare(element, value)`,
 * the argument order of .NET's search, which an asymmetric comparer observes.
 */
export function binarySearch<T>(
  array: ArrayLike<T>,
  start: number,
  length: number,
  value: T,
  compare: Comparison<T>,
): number {
  let low = start;
  let high = start + length - 1;

  while (low <= high) {
    const middle = low + ((high - low) >> 1);
    const order = compare(array[middle] as T, value);

    if (order === 0) {
      return middle;
    }

    if (order < 0) {
      low = middle + 1;
    } else {
      high = middle - 1;
    }
  }

  return ~low;
}
