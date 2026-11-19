/** `ICollection<T>.CopyTo(array, arrayIndex)` with .NET's checks, shared by the collections. Internal. */
import {
  throwArgumentNull,
  throwArrayTooSmall,
  throwNegativeDestinationIndex,
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
