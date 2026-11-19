// TEMPORARY test-only stand-in for R1's `set-algebra.ts` (see preload.ts).
import { throwArgumentNull } from "./throw-helpers.ts";

export interface SetLike<T> extends Iterable<T> {
  readonly count: number;
  contains(item: T): boolean;
}

type Distinct<T> = (items: Iterable<T>) => SetLike<T>;

const checkOther = <T>(other: Iterable<T>): Iterable<T> => {
  if (other === null || other === undefined) {
    throwArgumentNull("other");
  }

  return other;
};

const every = <T>(items: Iterable<T>, test: (item: T) => boolean): boolean => {
  for (const item of checkOther(items)) {
    if (!test(item)) {
      return false;
    }
  }

  return true;
};

export const isSubsetOf = <T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean => {
  const others = distinct(checkOther(other));

  return every(set, (item) => others.contains(item));
};

export const isSupersetOf = <T>(set: SetLike<T>, other: Iterable<T>): boolean => every(other, (item) => set.contains(item));

export const isProperSubsetOf = <T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean => {
  const others = distinct(checkOther(other));

  return others.count > set.count && every(set, (item) => others.contains(item));
};

export const isProperSupersetOf = <T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean => {
  const others = distinct(checkOther(other));

  return set.count > others.count && every(others, (item) => set.contains(item));
};

export const overlaps = <T>(set: SetLike<T>, other: Iterable<T>): boolean => !every(other, (item) => !set.contains(item));

export const setEquals = <T>(set: SetLike<T>, other: Iterable<T>, distinct: Distinct<T>): boolean => {
  const others = distinct(checkOther(other));

  return others.count === set.count && every(others, (item) => set.contains(item));
};
