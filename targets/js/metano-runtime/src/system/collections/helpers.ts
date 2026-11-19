/**
 * The free helpers the compiler lowers to (spec 011 FR-010, FR-021, FR-056;
 * contracts/runtime-collections.md § Helpers). Each is its own export, so a
 * bundle pulls only the ones it calls.
 */
import { IndexOutOfRangeException } from "../exceptions/index-out-of-range-exception.ts";
import type { IList, Span } from "./interfaces.ts";
import { List } from "./list.ts";
import { throwArgumentNull } from "./throw-helpers.ts";

/* `array[index] as T` in `fromEnd`: the unsigned compare above keeps `index` in range. */

/** `arr[^n]` on a native array (`T[]`, `JsArray<T>`): always checked. */
export function fromEnd<T>(array: readonly T[], n: number): T {
  const index = array.length - n;

  if (index >>> 0 >= array.length) {
    throw new IndexOutOfRangeException();
  }

  return array[index] as T;
}

/** Whether `value` is iterable (`Symbol.iterator` present; strings included). */
export function isIterable(value: unknown): value is Iterable<unknown> {
  return (
    value !== null &&
    value !== undefined &&
    typeof (value as { [Symbol.iterator]?: unknown })[Symbol.iterator] === "function"
  );
}

/**
 * The array behind a value that C# sees as an array: a native array, or the backing
 * array of a fixed-size adopted `List` (an array converted to a list interface, FR-003);
 * `null` otherwise.
 */
export function asArray(value: unknown): unknown[] | null {
  if (Array.isArray(value)) {
    return value;
  }

  return value instanceof List && value.isFixedSize ? value._items : null;
}

/** `list.AsJsArray()`: the backing array itself; `ArgumentNullException` for `null`. */
export function asJsArray<T>(list: List<T> | null | undefined): T[] {
  if (list === null || list === undefined) {
    throwArgumentNull("list");
  }

  return list._items;
}

/** `list.AsSpan()` / `CollectionsMarshal.AsSpan(list)`: the backing array; empty for `null`. */
export function asSpan<T>(list: List<T> | null | undefined): Span<T> {
  return list === null || list === undefined ? [] : list._items;
}

/** `list.AsReadOnlySpan()`: the backing array, read-only; empty for `null`. */
export function asReadOnlySpan<T>(list: List<T> | null | undefined): ReadonlyArray<T> {
  return list === null || list === undefined ? [] : list._items;
}

/**
 * `(IList<T>)value` (and `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`)
 * from a static type that may hold a native array (`object`, `IEnumerable<T>`): a native
 * array becomes a fixed-size list over the same storage, as C# views it; `null` /
 * `undefined` give `null`; any other value passes through untested (a user class
 * implementing `IList<T>` has no collection descriptor), so a value that is not a list
 * fails at its first use instead of at the cast.
 */
export function asList<T>(value: unknown): IList<T> | null {
  if (value === null || value === undefined) {
    return null;
  }

  return Array.isArray(value) ? List.adopt(value as T[], { fixedSize: true }) : (value as IList<T>);
}
