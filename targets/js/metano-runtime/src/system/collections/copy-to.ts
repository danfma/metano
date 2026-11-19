/** `ICollection<T>.CopyTo(array, arrayIndex)` with .NET's checks, shared by the collections. Internal. */
import {
  throwArgumentNull,
  throwArrayTooSmall,
  throwCollectionTooSmall,
  throwNegativeDestinationIndex,
  throwNegativeValue,
} from "./throw-helpers.ts";

/** Copies `items` (in the collection's enumeration order) into `array` from `arrayIndex`. */
export function copyInto<T>(items: readonly T[], array: T[], arrayIndex: number): void {
  if (array === null || array === undefined) {
    throwArgumentNull("destinationArray");
  }

  if (arrayIndex < 0) {
    throwNegativeDestinationIndex(arrayIndex);
  }

  if (array.length - arrayIndex < items.length) {
    throwArrayTooSmall();
  }

  for (let index = 0; index < items.length; index++) {
    array[arrayIndex + index] = items[index] as T;
  }
}

/**
 * The `CopyTo(array, index)` of `SortedSet`, `SortedDictionary` and `SortedList`:
 * the same copy with those classes' own .NET checks (`array`, `index ('-1') must
 * be a non-negative value.`, the collection-level "not long enough" text).
 */
export function copyIntoSorted<T>(items: readonly T[], array: T[], arrayIndex: number): void {
  if (array === null || array === undefined) {
    throwArgumentNull("array");
  }

  if (arrayIndex < 0) {
    throwNegativeValue("index", arrayIndex);
  }

  if (array.length - arrayIndex < items.length) {
    throwCollectionTooSmall();
  }

  for (let index = 0; index < items.length; index++) {
    array[arrayIndex + index] = items[index] as T;
  }
}
