/**
 * Runtime helpers for the C# List<T> / IList<T> / ICollection<T> family of methods
 * whose lowering can't be expressed as a simple inline JS expression. The compiler
 * imports these via the [MapMethod(..., RuntimeImports = "...")] declarative mappings
 * under Metano/Runtime/Lists.cs and ImmutableCollections.cs.
 *
 * The two flavors are kept in the same file because they share an underlying
 * representation (plain JS arrays); only the contract differs:
 *
 * - The mutable helpers (`listRemove`, future siblings) operate in place and return a
 *   status value, mirroring the C# `bool` return of `List<T>.Remove`.
 * - The immutable helpers (`immutableInsert`, `immutableRemoveAt`, `immutableRemove`)
 *   never mutate the input — they return a fresh array, mirroring the
 *   `ImmutableList<T>` / `ImmutableArray<T>` contract.
 */

/**
 * Mutating remove for arrays used as `List<T>` / `IList<T>` / `ICollection<T>`. Finds
 * the first occurrence of `item` (via `Array.indexOf`, so reference equality for
 * objects), splices it out in place, and returns true if anything was removed.
 * Returns false when the item isn't present, leaving `arr` untouched.
 *
 * Mirrors `System.Collections.Generic.List<T>.Remove(T item)` semantics.
 */
export function listRemove<T>(arr: T[], item: T): boolean {
  const i = arr.indexOf(item);
  if (i < 0) return false;
  arr.splice(i, 1);
  return true;
}

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
