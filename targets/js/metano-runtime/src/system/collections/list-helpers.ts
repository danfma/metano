/**
 * Runtime helpers for the C# List<T> / IList<T> / ICollection<T> family of methods
 * whose lowering can't be expressed as a simple inline JS expression. The compiler
 * imports these via the [MapMethod(..., RuntimeImports = "...")] declarative mappings
 * under Metano/Runtime/Lists.cs and ImmutableCollections.cs.
 *
 * The immutable helpers (`immutableInsert`, `immutableRemoveAt`, `immutableRemove`)
 * never mutate the input — they return a fresh array, mirroring the
 * `ImmutableList<T>` / `ImmutableArray<T>` contract.
 */

/**
 * Returns a new array with `item` inserted at `index`. The original array is left
 * untouched. Mirrors `ImmutableList<T>.Insert(int index, T item)` and
 * `ImmutableArray<T>.Insert(int index, T item)`.
 */
export function immutableInsert<T>(arr: readonly T[], index: number, item: T): T[] {
  return [...arr.slice(0, index), item, ...arr.slice(index)];
}

/**
 * Returns a new array with the element at `index` removed. The original array is left
 * untouched. Mirrors `ImmutableList<T>.RemoveAt(int index)` and
 * `ImmutableArray<T>.RemoveAt(int index)`.
 */
export function immutableRemoveAt<T>(arr: readonly T[], index: number): T[] {
  return [...arr.slice(0, index), ...arr.slice(index + 1)];
}

/**
 * Returns a new array with the first occurrence of `item` removed, or the original
 * array unchanged when the item is not present. Mirrors `ImmutableList<T>.Remove(T)`
 * and `ImmutableArray<T>.Remove(T)`, both of which return the receiver when the
 * lookup misses (since immutable instances can be safely shared).
 */
export function immutableRemove<T>(arr: readonly T[], item: T): readonly T[] {
  const i = arr.indexOf(item);
  if (i < 0) return arr;
  return [...arr.slice(0, i), ...arr.slice(i + 1)];
}
