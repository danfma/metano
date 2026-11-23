/**
 * The free helpers an array or a span lowers to (#663, #664): the default order of
 * `Array.Sort` / `MemoryExtensions.Sort`, and the value-equality searches of
 * `Array.IndexOf` / `MemoryExtensions.Contains`. JavaScript's own `sort()` orders by text
 * and `indexOf` compares by identity, so neither is what C# computes.
 */
import { type ComparisonArgument, comparisonOf, elementEquals } from "./default-comparer.ts";
import {
  throwArgumentNull,
  throwArrayTooSmall,
  throwCompareFailed,
  throwIndexAtMostCount,
} from "./throw-helpers.ts";

/** Primitives other than `NaN` compare by `===`, which `Array#indexOf` uses. */
export function isIdentityComparable(value: unknown): boolean {
  return typeof value !== "object" && !Number.isNaN(value);
}

/**
 * Sorts `items` in place (stable) with `comparison` or `Comparer<T>.Default`; a comparison
 * that throws surfaces as `InvalidOperationException`, as `Array.Sort` reports it.
 */
export function sortInPlace<T>(items: T[], comparison?: ComparisonArgument<T>): void {
  const compare = comparisonOf(comparison);

  try {
    items.sort(compare);
  } catch (error) {
    throwCompareFailed(error);
  }
}

/** `Array.Sort(array)`, `Array.Sort(array, comparison | comparer)`, `span.Sort(...)`. */
export function sortArray<T>(
  array: T[] | null | undefined,
  comparison?: ComparisonArgument<T>,
): void {
  if (array === null || array === undefined) {
    throwArgumentNull("array");
  }

  if (array.length > 1) {
    sortInPlace(array, comparison);
  }
}

/** `Array.Sort(keys, items, comparer?)`: both arrays are permuted by the order of the keys. */
export function sortArrayByKeys<K, V>(
  keys: K[] | null | undefined,
  items: V[] | null | undefined,
  comparison?: ComparisonArgument<K>,
): void {
  if (keys === null || keys === undefined) {
    throwArgumentNull("keys");
  }

  if (items === null || items === undefined) {
    sortArray(keys, comparison);

    return;
  }

  if (items.length < keys.length) {
    throwArrayTooSmall();
  }

  const compare = comparisonOf(comparison);
  const order = keys.map((_, index) => index);

  try {
    order.sort((a, b) => compare(keys[a] as K, keys[b] as K));
  } catch (error) {
    throwCompareFailed(error);
  }

  const sortedKeys = order.map((index) => keys[index] as K);
  const sortedItems = order.map((index) => items[index] as V);

  sortedKeys.forEach((key, index) => {
    keys[index] = key;
    items[index] = sortedItems[index] as V;
  });
}

/**
 * `Array.IndexOf(array, value, start?)` and the span search: `EqualityComparer<T>.Default`
 * semantics (a record, a decimal or a date compares by value, `NaN` finds `NaN`). A string
 * receiver, which a `ReadOnlySpan<char>` over text is, searches its characters.
 */
export function arrayIndexOf<T>(array: readonly T[] | string, value: T, start?: number): number {
  const from = start ?? 0;

  if (typeof array === "string") {
    return array.indexOf(value as string, from);
  }

  if (from >>> 0 > array.length) {
    throwIndexAtMostCount();
  }

  if (isIdentityComparable(value)) {
    return array.indexOf(value, from);
  }

  for (let position = from; position < array.length; position++) {
    if (elementEquals(array[position], value)) {
      return position;
    }
  }

  return -1;
}

/** `Array.LastIndexOf(array, value)` and the span search; see {@link arrayIndexOf}. */
export function arrayLastIndexOf<T>(array: readonly T[] | string, value: T): number {
  if (typeof array === "string") {
    return array.lastIndexOf(value as string);
  }

  if (isIdentityComparable(value)) {
    return array.lastIndexOf(value);
  }

  for (let position = array.length - 1; position >= 0; position--) {
    if (elementEquals(array[position], value)) {
      return position;
    }
  }

  return -1;
}

/** `span.Contains(value)` and `array.Contains(value)`; see {@link arrayIndexOf}. */
export function arrayContains<T>(array: readonly T[] | string, value: T): boolean {
  return typeof array === "string"
    ? array.includes(value as string)
    : arrayIndexOf(array, value) >= 0;
}
