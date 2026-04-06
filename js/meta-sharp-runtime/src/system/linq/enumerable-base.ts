import { HashSet } from "../collections/hash-set.ts";

/**
 * Abstract base for all LINQ enumerable operations.
 * Each subclass represents a node in the query composition tree.
 * Lazy: operations are deferred until iteration (via Symbol.iterator).
 */
export abstract class EnumerableBase<T> implements Iterable<T> {
  abstract [Symbol.iterator](): Iterator<T>;

  // ─── Composition (lazy) ─────────────────────────────────

  where(predicate: (item: T, index: number) => boolean): EnumerableBase<T> {
    // Avoid circular import — use dynamic import pattern via factory
    return createWhere(this, predicate);
  }

  select<R>(selector: (item: T, index: number) => R): EnumerableBase<R> {
    return createSelect(this, selector);
  }

  selectMany<R>(selector: (item: T, index: number) => Iterable<R>): EnumerableBase<R> {
    return createSelectMany(this, selector);
  }

  orderBy<K>(keySelector: (item: T) => K): EnumerableBase<T> {
    return createOrderBy(this, keySelector, false);
  }

  orderByDescending<K>(keySelector: (item: T) => K): EnumerableBase<T> {
    return createOrderBy(this, keySelector, true);
  }

  thenBy<K>(keySelector: (item: T) => K): EnumerableBase<T> {
    return createThenBy(this, keySelector, false);
  }

  thenByDescending<K>(keySelector: (item: T) => K): EnumerableBase<T> {
    return createThenBy(this, keySelector, true);
  }

  take(count: number): EnumerableBase<T> {
    return createTake(this, count);
  }

  skip(count: number): EnumerableBase<T> {
    return createSkip(this, count);
  }

  distinct(): EnumerableBase<T> {
    return createDistinct(this);
  }

  groupBy<K>(keySelector: (item: T) => K): EnumerableBase<Grouping<K, T>> {
    return createGroupBy(this, keySelector);
  }

  concat(other: Iterable<T>): EnumerableBase<T> {
    return createConcat(this, other);
  }

  takeWhile(predicate: (item: T, index: number) => boolean): EnumerableBase<T> {
    return createTakeWhile(this, predicate);
  }

  skipWhile(predicate: (item: T, index: number) => boolean): EnumerableBase<T> {
    return createSkipWhile(this, predicate);
  }

  distinctBy<K>(keySelector: (item: T) => K): EnumerableBase<T> {
    return createDistinctBy(this, keySelector);
  }

  reverse(): EnumerableBase<T> {
    return createReverse(this);
  }

  zip<U, R>(other: Iterable<U>, resultSelector: (first: T, second: U) => R): EnumerableBase<R> {
    return createZip(this, other, resultSelector);
  }

  append(element: T): EnumerableBase<T> {
    return createAppend(this, element);
  }

  prepend(element: T): EnumerableBase<T> {
    return createPrepend(this, element);
  }

  union(other: Iterable<T>): EnumerableBase<T> {
    return createUnion(this, other);
  }

  intersect(other: Iterable<T>): EnumerableBase<T> {
    return createIntersect(this, other);
  }

  except(other: Iterable<T>): EnumerableBase<T> {
    return createExcept(this, other);
  }

  // ─── Terminal operations (eager) ────────────────────────

  toArray(): T[] {
    return [...this];
  }

  toMap<K, V>(keySelector: (item: T) => K, valueSelector: (item: T) => V): Map<K, V> {
    const map = new Map<K, V>();
    for (const item of this) {
      map.set(keySelector(item), valueSelector(item));
    }
    return map;
  }

  toSet(): HashSet<T> {
    return new HashSet(this);
  }

  first(predicate?: (item: T) => boolean): T {
    for (const item of this) {
      if (!predicate || predicate(item)) return item;
    }
    throw new Error("Sequence contains no matching element");
  }

  firstOrDefault(predicate?: (item: T) => boolean, defaultValue: T | null = null): T | null {
    for (const item of this) {
      if (!predicate || predicate(item)) return item;
    }
    return defaultValue;
  }

  last(predicate?: (item: T) => boolean): T {
    let result: T | undefined;
    let found = false;
    for (const item of this) {
      if (!predicate || predicate(item)) {
        result = item;
        found = true;
      }
    }
    if (!found) throw new Error("Sequence contains no matching element");
    return result!;
  }

  lastOrDefault(predicate?: (item: T) => boolean, defaultValue: T | null = null): T | null {
    let result: T | null = defaultValue;
    for (const item of this) {
      if (!predicate || predicate(item)) result = item;
    }
    return result;
  }

  single(predicate?: (item: T) => boolean): T {
    let result: T | undefined;
    let count = 0;
    for (const item of this) {
      if (!predicate || predicate(item)) {
        result = item;
        count++;
        if (count > 1) throw new Error("Sequence contains more than one matching element");
      }
    }
    if (count === 0) throw new Error("Sequence contains no matching element");
    return result!;
  }

  singleOrDefault(predicate?: (item: T) => boolean, defaultValue: T | null = null): T | null {
    let result: T | null = defaultValue;
    let count = 0;
    for (const item of this) {
      if (!predicate || predicate(item)) {
        result = item;
        count++;
        if (count > 1) throw new Error("Sequence contains more than one matching element");
      }
    }
    return result;
  }

  any(predicate?: (item: T) => boolean): boolean {
    for (const item of this) {
      if (!predicate || predicate(item)) return true;
    }
    return false;
  }

  all(predicate: (item: T) => boolean): boolean {
    for (const item of this) {
      if (!predicate(item)) return false;
    }
    return true;
  }

  count(predicate?: (item: T) => boolean): number {
    let n = 0;
    for (const item of this) {
      if (!predicate || predicate(item)) n++;
    }
    return n;
  }

  sum(selector?: (item: T) => number): number {
    let total = 0;
    for (const item of this) {
      total += selector ? selector(item) : (item as unknown as number);
    }
    return total;
  }

  min(selector?: (item: T) => number): number {
    let result = Infinity;
    for (const item of this) {
      const value = selector ? selector(item) : (item as unknown as number);
      if (value < result) result = value;
    }
    return result;
  }

  max(selector?: (item: T) => number): number {
    let result = -Infinity;
    for (const item of this) {
      const value = selector ? selector(item) : (item as unknown as number);
      if (value > result) result = value;
    }
    return result;
  }

  average(selector?: (item: T) => number): number {
    let total = 0;
    let count = 0;
    for (const item of this) {
      total += selector ? selector(item) : (item as unknown as number);
      count++;
    }
    if (count === 0) throw new Error("Sequence contains no elements");
    return total / count;
  }

  minBy<K>(selector: (item: T) => K): T {
    let result: T | undefined;
    let minKey: K | undefined;
    let found = false;
    for (const item of this) {
      const key = selector(item);
      if (!found || key < minKey!) {
        result = item;
        minKey = key;
        found = true;
      }
    }
    if (!found) throw new Error("Sequence contains no elements");
    return result!;
  }

  maxBy<K>(selector: (item: T) => K): T {
    let result: T | undefined;
    let maxKey: K | undefined;
    let found = false;
    for (const item of this) {
      const key = selector(item);
      if (!found || key > maxKey!) {
        result = item;
        maxKey = key;
        found = true;
      }
    }
    if (!found) throw new Error("Sequence contains no elements");
    return result!;
  }

  contains(item: T): boolean {
    for (const element of this) {
      if (element === item) return true;
    }
    return false;
  }

  aggregate<A>(seed: A, accumulator: (acc: A, item: T) => A): A {
    let result = seed;
    for (const item of this) {
      result = accumulator(result, item);
    }
    return result;
  }

  forEach(action: (item: T, index: number) => void): void {
    let i = 0;
    for (const item of this) {
      action(item, i++);
    }
  }
}

/** Represents a group of elements that share a common key. */
export interface Grouping<K, T> extends Iterable<T> {
  readonly key: K;
}

// ─── Factory function references (set by linq/index.ts to avoid circular imports) ─

export let createWhere: <T>(source: EnumerableBase<T>, predicate: (item: T, index: number) => boolean) => EnumerableBase<T>;
export let createSelect: <T, R>(source: EnumerableBase<T>, selector: (item: T, index: number) => R) => EnumerableBase<R>;
export let createSelectMany: <T, R>(source: EnumerableBase<T>, selector: (item: T, index: number) => Iterable<R>) => EnumerableBase<R>;
export let createOrderBy: <T, K>(source: EnumerableBase<T>, keySelector: (item: T) => K, descending: boolean) => EnumerableBase<T>;
export let createTake: <T>(source: EnumerableBase<T>, count: number) => EnumerableBase<T>;
export let createSkip: <T>(source: EnumerableBase<T>, count: number) => EnumerableBase<T>;
export let createDistinct: <T>(source: EnumerableBase<T>) => EnumerableBase<T>;
export let createGroupBy: <T, K>(source: EnumerableBase<T>, keySelector: (item: T) => K) => EnumerableBase<Grouping<K, T>>;
export let createConcat: <T>(first: EnumerableBase<T>, second: Iterable<T>) => EnumerableBase<T>;
export let createThenBy: <T, K>(source: EnumerableBase<T>, keySelector: (item: T) => K, descending: boolean) => EnumerableBase<T>;
export let createTakeWhile: <T>(source: EnumerableBase<T>, predicate: (item: T, index: number) => boolean) => EnumerableBase<T>;
export let createSkipWhile: <T>(source: EnumerableBase<T>, predicate: (item: T, index: number) => boolean) => EnumerableBase<T>;
export let createDistinctBy: <T, K>(source: EnumerableBase<T>, keySelector: (item: T) => K) => EnumerableBase<T>;
export let createReverse: <T>(source: EnumerableBase<T>) => EnumerableBase<T>;
export let createZip: <T, U, R>(first: EnumerableBase<T>, second: Iterable<U>, resultSelector: (first: T, second: U) => R) => EnumerableBase<R>;
export let createAppend: <T>(source: EnumerableBase<T>, element: T) => EnumerableBase<T>;
export let createPrepend: <T>(source: EnumerableBase<T>, element: T) => EnumerableBase<T>;
export let createUnion: <T>(first: EnumerableBase<T>, second: Iterable<T>) => EnumerableBase<T>;
export let createIntersect: <T>(first: EnumerableBase<T>, second: Iterable<T>) => EnumerableBase<T>;
export let createExcept: <T>(first: EnumerableBase<T>, second: Iterable<T>) => EnumerableBase<T>;

export function _registerFactories(factories: {
  where: typeof createWhere;
  select: typeof createSelect;
  selectMany: typeof createSelectMany;
  orderBy: typeof createOrderBy;
  take: typeof createTake;
  skip: typeof createSkip;
  distinct: typeof createDistinct;
  groupBy: typeof createGroupBy;
  concat: typeof createConcat;
  thenBy: typeof createThenBy;
  takeWhile: typeof createTakeWhile;
  skipWhile: typeof createSkipWhile;
  distinctBy: typeof createDistinctBy;
  reverse: typeof createReverse;
  zip: typeof createZip;
  append: typeof createAppend;
  prepend: typeof createPrepend;
  union: typeof createUnion;
  intersect: typeof createIntersect;
  except: typeof createExcept;
}) {
  createWhere = factories.where;
  createSelect = factories.select;
  createSelectMany = factories.selectMany;
  createOrderBy = factories.orderBy;
  createTake = factories.take;
  createSkip = factories.skip;
  createDistinct = factories.distinct;
  createGroupBy = factories.groupBy;
  createConcat = factories.concat;
  createThenBy = factories.thenBy;
  createTakeWhile = factories.takeWhile;
  createSkipWhile = factories.skipWhile;
  createDistinctBy = factories.distinctBy;
  createReverse = factories.reverse;
  createZip = factories.zip;
  createAppend = factories.append;
  createPrepend = factories.prepend;
  createUnion = factories.union;
  createIntersect = factories.intersect;
  createExcept = factories.except;
}
