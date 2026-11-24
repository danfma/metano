/**
 * The BCL collection interfaces as TypeScript interfaces (spec 011 FR-003,
 * contracts/runtime-collections.md § Interfaces). Types only: importing this
 * module costs nothing at run time.
 */
export interface IReadOnlyCollection<T> extends Iterable<T> {
  readonly count: number;
}
export interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
  /** Mirrors `count` for JavaScript/TypeScript array ergonomics. */
  readonly length: number;
  get(index: number): T; // ArgumentOutOfRangeException
  /** Detached shallow copy of the list's elements (spec 011 FR-055). */
  toArray(): T[];
  /**
   * Read-only view over the backing JS array (zero-copy for array-backed list kinds;
   * spec 011 FR-056). Reflects later mutations of a mutable source; read-only for the consumer.
   */
  asJsArray(): readonly T[];
}
export interface ICollection<T> extends Iterable<T> {
  readonly count: number;
  readonly isReadOnly: boolean;
  add(item: T): void;
  clear(): void;
  contains(item: T): boolean;
  copyTo(array: T[], arrayIndex?: number): void;
  remove(item: T): boolean;
}
export interface IList<T> extends ICollection<T> {
  get(index: number): T;
  set(index: number, value: T): void;
  indexOf(item: T): number;
  insert(index: number, item: T): void;
  removeAt(index: number): void;
}
export interface IReadOnlyDictionary<K, V> extends IReadOnlyCollection<readonly [K, V]> {
  get(key: K): V; // KeyNotFoundException; ArgumentNullException for null
  readonly keys: IReadOnlyCollection<K>; // C#: IEnumerable<K>
  readonly values: IReadOnlyCollection<V>; // C#: IEnumerable<V>
  containsKey(key: K): boolean;
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D];
}
export interface IDictionary<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number; // ICollection<KeyValuePair<K, V>>
  readonly isReadOnly: boolean;
  clear(): void;
  get(key: K): V;
  set(key: K, value: V): void; // indexer write: insert or overwrite
  readonly keys: IReadOnlyCollection<K>; // C#: ICollection<K>; its mutators are not declared (they throw on .NET)
  readonly values: IReadOnlyCollection<V>;
  add(key: K, value: V): void; // ArgumentException on a duplicate
  containsKey(key: K): boolean;
  remove(key: K): boolean;
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D];
}
export interface IReadOnlySet<T> extends IReadOnlyCollection<T> {
  contains(item: T): boolean;
  isSubsetOf(other: Iterable<T>): boolean;
  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;
  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;
  setEquals(other: Iterable<T>): boolean;
}
export interface ISet<T> extends ICollection<T> {
  add(item: T): boolean; // ISet<T>.Add returns bool; assignable to ICollection's void add
  unionWith(other: Iterable<T>): void;
  intersectWith(other: Iterable<T>): void;
  exceptWith(other: Iterable<T>): void;
  symmetricExceptWith(other: Iterable<T>): void;
  isSubsetOf(other: Iterable<T>): boolean;
  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;
  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;
  setEquals(other: Iterable<T>): boolean;
}
// A view of a List's backing array (FR-056): index writes and the in-place members that keep the length;
// push / pop / shift / unshift / splice and a length assignment do not type-check (tsgo --strict, research R16).
export interface Span<T> extends ReadonlyArray<T> {
  [index: number]: T;
  sort(compareFn?: (a: T, b: T) => number): this;
  reverse(): this;
  fill(value: T, start?: number, end?: number): this;
  copyWithin(target: number, start: number, end?: number): this;
}
