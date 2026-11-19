import { defaultCompare, type IComparer } from "./default-comparer.ts";
import type { CompareFunction } from "./red-black-tree.ts";
import { throwArgumentNull } from "./throw-helpers.ts";

/**
 * The pieces `SortedSet`, `SortedDictionary` and `SortedList` share (stage
 * 011h). Internal: not re-exported by the `system/collections` barrel.
 */

/** The compare function of a sorted class: the argument comparer, or `defaultCompare` (`Comparer<T>.Default`). */
export function compareFunctionOf<T>(comparer: IComparer<T> | null): CompareFunction<T> {
  return comparer === null
    ? (defaultCompare as CompareFunction<T>)
    : (x, y) => comparer.compare(x, y);
}

/** Defines one storage field: non-enumerable and non-configurable (the storage ABI of FR-002). */
export function defineField(target: object, name: string, value: unknown, writable: boolean): void {
  Object.defineProperty(target, name, { value, writable, enumerable: false, configurable: false });
}

/** `key`, or `ArgumentNullException` (`key`) for `null` / `undefined`. */
export function checkedKey<K>(key: K): K {
  if (key === null || key === undefined) {
    throwArgumentNull("key");
  }

  return key;
}
