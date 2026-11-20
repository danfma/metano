/**
 * The immutable collection interfaces (spec 011 FR-054). Types only, 0 B:
 * each declares only the members its C# interface declares (W4-13); the
 * extension-method forms fill the optional arguments.
 */
import type { IEqualityComparer } from "./default-comparer.ts";
import type { IReadOnlyCollection, IReadOnlyDictionary, IReadOnlyList } from "./interfaces.ts";

export interface IImmutableList<T> extends IReadOnlyList<T> {
  add(value: T): IImmutableList<T>;
  addRange(items: Iterable<T>): IImmutableList<T>;
  clear(): IImmutableList<T>;
  insert(index: number, element: T): IImmutableList<T>;
  insertRange(index: number, items: Iterable<T>): IImmutableList<T>;
  remove(value: T, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;
  removeAll(match: (item: T) => boolean): IImmutableList<T>;
  removeAt(index: number): IImmutableList<T>;
  removeRange(index: number, count: number): IImmutableList<T>;
  removeItems(items: Iterable<T>, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;
  replace(oldValue: T, newValue: T, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;
  setItem(index: number, value: T): IImmutableList<T>;
  indexOf(item: T, index?: number, count?: number, comparer?: IEqualityComparer<T> | null): number;
  lastIndexOf(
    item: T,
    index?: number,
    count?: number,
    comparer?: IEqualityComparer<T> | null,
  ): number;
}
export interface IImmutableSet<T> extends IReadOnlyCollection<T> {
  // C#: IReadOnlyCollection<T>, not IReadOnlySet<T>
  contains(value: T): boolean;
  isSubsetOf(other: Iterable<T>): boolean;
  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;
  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;
  setEquals(other: Iterable<T>): boolean;
  add(value: T): IImmutableSet<T>;
  remove(value: T): IImmutableSet<T>;
  clear(): IImmutableSet<T>;
  union(other: Iterable<T>): IImmutableSet<T>;
  intersect(other: Iterable<T>): IImmutableSet<T>;
  except(other: Iterable<T>): IImmutableSet<T>;
  symmetricExcept(other: Iterable<T>): IImmutableSet<T>;
  tryGetValue(equalValue: T, defaultValue?: unknown): readonly [boolean, T];
}
export interface IImmutableDictionary<K, V> extends IReadOnlyDictionary<K, V> {
  add(key: K, value: V): IImmutableDictionary<K, V>;
  addRange(pairs: Iterable<readonly [K, V]>): IImmutableDictionary<K, V>;
  setItem(key: K, value: V): IImmutableDictionary<K, V>;
  setItems(pairs: Iterable<readonly [K, V]>): IImmutableDictionary<K, V>;
  remove(key: K): IImmutableDictionary<K, V>;
  removeRange(keys: Iterable<K>): IImmutableDictionary<K, V>;
  clear(): IImmutableDictionary<K, V>;
  contains(pair: readonly [K, V]): boolean;
  tryGetKey(equalKey: K, defaultValue?: unknown): readonly [boolean, K];
}
