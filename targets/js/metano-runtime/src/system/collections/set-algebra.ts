/**
 * The read-only `ISet<T>` / `IReadOnlySet<T>` algorithms, written once over
 * `SetLike<T>` and shared by `HashSet` and (stage 011h) the sorted and
 * immutable sets (contracts/runtime-collections.md § `set-algebra.ts`).
 * Internal. `distinct` builds a set with the receiver's comparer, so `other`
 * is deduplicated by the receiver's rule, as .NET does.
 */
import { throwArgumentNull } from "./throw-helpers.ts";

export interface SetLike<T> extends Iterable<T> {
  readonly count: number;
  contains(item: T): boolean;
}

export type Distinct<T> = (items: Iterable<T>) => SetLike<T>;

/** Every element of `set` is in `other`. */
export function isSubsetOf<T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean {
  const others = distinct(requireOther(other));

  return set.count <= others.count && everyIn(set, others);
}

/** Every element of `other` is in `set`. */
export function isSupersetOf<T>(set: SetLike<T>, other: Iterable<T>): boolean {
  for (const item of requireOther(other)) {
    if (!set.contains(item)) {
      return false;
    }
  }

  return true;
}

export function isProperSubsetOf<T>(
  set: SetLike<T>,
  other: Iterable<T>,
  distinct: Distinct<T>,
): boolean {
  const others = distinct(requireOther(other));

  return set.count < others.count && everyIn(set, others);
}

export function isProperSupersetOf<T>(
  set: SetLike<T>,
  other: Iterable<T>,
  distinct: Distinct<T>,
): boolean {
  const others = distinct(requireOther(other));

  return set.count > others.count && everyIn(others, set);
}

/** At least one element is in both. */
export function overlaps<T>(set: SetLike<T>, other: Iterable<T>): boolean {
  for (const item of requireOther(other)) {
    if (set.contains(item)) {
      return true;
    }
  }

  return false;
}

/** The same elements, ignoring order and duplicates in `other`. */
export function setEquals<T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean {
  const others = distinct(requireOther(other));

  return set.count === others.count && everyIn(others, set);
}

function everyIn<T>(items: Iterable<T>, set: SetLike<T>): boolean {
  for (const item of items) {
    if (!set.contains(item)) {
      return false;
    }
  }

  return true;
}

function requireOther<T>(other: Iterable<T>): Iterable<T> {
  if (other === null || other === undefined) {
    throwArgumentNull("other");
  }

  return other;
}
