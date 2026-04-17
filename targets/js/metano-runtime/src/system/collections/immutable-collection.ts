/**
 * Pure helper functions for immutable array operations. Each function takes a
 * source array and returns a NEW array — the original is never mutated. This
 * mirrors the contract of C#'s `ImmutableList<T>` / `ImmutableArray<T>`.
 *
 * The compiler's declarative mappings in `Metano/Runtime/ImmutableCollections.cs`
 * lower C# calls like `list.Add(item)` to `ImmutableCollection.add(list, item)`.
 * Using a namespace keeps the generated code readable and the helpers debuggable
 * (named functions in stack traces), while the underlying representation stays as
 * a plain JS array — no wrapper, no serialization friction.
 */
export namespace ImmutableCollection {
  /** Returns a new array with `item` appended at the end. */
  export function add<T>(array: readonly T[], item: T): T[] {
    return [...array, item];
  }

  /** Returns a new array with all items from `other` appended. */
  export function addRange<T>(array: readonly T[], other: readonly T[]): T[] {
    return [...array, ...other];
  }

  /** Returns a new array with `item` inserted at `index`. */
  export function insert<T>(array: readonly T[], index: number, item: T): T[] {
    return [...array.slice(0, index), item, ...array.slice(index)];
  }

  /** Returns a new array with the element at `index` removed. */
  export function removeAt<T>(array: readonly T[], index: number): T[] {
    return [...array.slice(0, index), ...array.slice(index + 1)];
  }

  /**
   * Returns a new array with the first occurrence of `item` removed, or the
   * original array unchanged when the item is not found. Uses reference
   * equality (indexOf) — same as the C# default equality for reference types.
   */
  export function remove<T>(array: readonly T[], item: T): readonly T[] {
    const i = array.indexOf(item);
    if (i < 0) return array;
    return [...array.slice(0, i), ...array.slice(i + 1)];
  }

  /** Returns an empty array. Equivalent to `ImmutableList<T>.Clear()`. */
  export function clear<T>(): T[] {
    return [];
  }

  /** Returns a new array with the element at `index` replaced by `item`. */
  export function set<T>(array: readonly T[], index: number, item: T): T[] {
    const copy = [...array];
    copy[index] = item;
    return copy;
  }
}
