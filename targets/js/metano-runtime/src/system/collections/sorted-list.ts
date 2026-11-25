import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto, copyIntoSorted } from "./copy-to.ts";
import {
  type Comparison,
  comparisonOf,
  defaultComparerFor,
  elementEquals,
  type IComparer,
} from "./default-comparer.ts";
import type { IDictionary, IList, IReadOnlyDictionary, IReadOnlyList } from "./interfaces.ts";
import { binarySearch } from "./sorted-array.ts";
import {
  requireKey,
  throwArgumentNull,
  throwCompareFailed,
  throwDuplicateKey,
  throwEnumeratorModified,
  throwKeyListWrite,
  throwKeyNotFound,
  throwNegativeValue,
  throwOOR,
  throwSortedListNested,
} from "./throw-helpers.ts";

/**
 * What a `SortedListView` and the iterator read from their list. Method syntax
 * keeps every `SortedList<K, V>` assignable to it whatever its type arguments.
 */
interface SortedListSource {
  readonly _keys: readonly unknown[];
  readonly _values: readonly unknown[];
  readonly _version: number;
  readonly count: number;
  checkIndex(index: number): void;
  indexOfKey(key: unknown): number;
  indexOfValue(value: unknown): number;
}

let SortedList$rt: CollectionType<SortedList<unknown, unknown>> | undefined;
let SortedListView$rt: CollectionType<SortedListView<unknown>> | undefined;

/**
 * Mirrors `System.Collections.Generic.SortedList<TKey, TValue>` (spec 011
 * FR-054): two parallel arrays sorted by the key comparer, searched with
 * .NET's binary search. O(log n) lookups, O(n) inserts and removals, O(1)
 * index access, as .NET's own complexity.
 *
 * `add`, `set` (a new or an existing key), `removeAt`, `setValueAtIndex` and
 * `clear` bump the version; `remove` bumps only when it finds the key, as
 * .NET does.
 */
export class SortedList<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  declare readonly _keys: K[];
  declare readonly _values: V[];
  declare _version: number;
  declare readonly _comparer: IComparer<K> | null;
  declare readonly _compare: Comparison<K>;
  declare _keyList: SortedListView<K> | undefined;
  declare _valueList: SortedListView<V> | undefined;
  declare readonly $typeArguments$SortedList: readonly RuntimeType[] | undefined;

  constructor(
    source?: Iterable<readonly [K, V]> | null,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    const ownComparer = comparer ?? null;

    Object.defineProperty(this, "_keys", { value: [] });
    Object.defineProperty(this, "_values", { value: [] });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "_comparer", { value: ownComparer });
    Object.defineProperty(this, "_compare", { value: comparisonOf(ownComparer) });
    Object.defineProperty(this, "_keyList", { value: undefined, writable: true });
    Object.defineProperty(this, "_valueList", { value: undefined, writable: true });
    Object.defineProperty(this, "$typeArguments$SortedList", { value: typeArguments ?? undefined });

    if (source !== null && source !== undefined) {
      this.copyFrom(source);
    }
  }

  /** `new SortedList<K, V>(dictionary[, comparer])`: a copy; `ArgumentNullException` (`dictionary`) for `null`. */
  static from<K, V>(
    source: Iterable<readonly [K, V]> | null | undefined,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedList<K, V> {
    if (source === null || source === undefined) {
      throwArgumentNull("dictionary");
    }

    return new SortedList(source, comparer, typeArguments);
  }

  /** `new SortedList<K, V>(capacity[, comparer])`: the capacity is validated, then dropped. */
  static withCapacity<K, V>(
    capacity: number,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedList<K, V> {
    if (capacity < 0) {
      throwNegativeValue("capacity", capacity);
    }

    return new SortedList(null, comparer, typeArguments);
  }

  /** The `{ { k, v } }` initializer: `add` semantics, a duplicate key throws. */
  static of<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedList<K, V> {
    const list = new SortedList<K, V>(null, comparer, typeArguments);

    for (const [key, value] of entries) {
      list.add(key, value);
    }

    return list;
  }

  /** The `{ [k] = v }` initializer (and JSON): indexer semantics, the last value wins. */
  static assign<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedList<K, V> {
    const list = new SortedList<K, V>(null, comparer, typeArguments);

    for (const [key, value] of entries) {
      list.set(key, value);
    }

    return list;
  }

  static get $runtimeType(): CollectionType<SortedList<unknown, unknown>> {
    return (SortedList$rt ??= {
      kind: "collection",
      collection: "SortedList",
      name: "SortedList",
      fullName: "System.Collections.Generic.SortedList",
      namespace: "System.Collections.Generic",
      ctor: SortedList,
      typeArguments: [],
      variance: ["invariant", "invariant"],
      interfaces: [
        { collection: "IDictionary", arguments: [0, 1] },
        { collection: "IReadOnlyDictionary", arguments: [0, 1] },
        { collection: "ICollection", arguments: null },
        { collection: "IReadOnlyCollection", arguments: null },
      ],
      test: (value: unknown): value is SortedList<unknown, unknown> => value instanceof SortedList,
      closeFor: (instance) => closeCollectionFor(SortedList.$runtimeType, instance),
    });
  }

  get count(): number {
    return this._keys.length;
  }

  get isReadOnly(): false {
    return false;
  }

  /** The argument comparer, or `defaultComparer` (`Comparer<K>.Default`). */
  get comparer(): IComparer<K> {
    return this._comparer ?? defaultComparerFor<K>();
  }

  /** `Keys`: a live read-only list of the keys, in order. */
  get keys(): SortedListView<K> {
    return (this._keyList ??= new SortedListView<K>(this, true, this.viewTypeArguments(0)));
  }

  /** `Values`: a live read-only list of the values, in key order. */
  get values(): SortedListView<V> {
    return (this._valueList ??= new SortedListView<V>(this, false, this.viewTypeArguments(1)));
  }

  get(key: K): V {
    const index = this.indexOfKey(key);

    if (index < 0) {
      throwKeyNotFound(key);
    }

    return at(this._values, index);
  }

  set(key: K, value: V): void {
    const index = this.search(requireKey(key));

    if (index >= 0) {
      this._values[index] = value;
      this.bumpVersion();

      return;
    }

    this.insertAt(~index, key, value);
  }

  /** `list[k] op= …`: read (`KeyNotFoundException` on a miss), apply, write; returns the new value. */
  update(key: K, f: (current: V) => V): V {
    const value = f(this.get(key));

    this.set(key, value);

    return value;
  }

  /** `list[k]++` in expression position: as `update`, returning the old value. */
  updateReturningOld(key: K, f: (current: V) => V): V {
    const current = this.get(key);

    this.set(key, f(current));

    return current;
  }

  add(key: K, value: V): void {
    const index = this.search(requireKey(key));

    if (index >= 0) {
      throwDuplicateKey(key, "key");
    }

    this.insertAt(~index, key, value);
  }

  remove(key: K): boolean {
    const index = this.indexOfKey(key);

    if (index < 0) {
      return false;
    }

    this.removeAt(index);

    return true;
  }

  removeAt(index: number): void {
    this.checkIndex(index);
    this._keys.splice(index, 1);
    this._values.splice(index, 1);
    this.bumpVersion();
  }

  clear(): void {
    this._keys.length = 0;
    this._values.length = 0;
    this.bumpVersion();
  }

  containsKey(key: K): boolean {
    return this.indexOfKey(key) >= 0;
  }

  containsValue(value: V): boolean {
    return this.indexOfValue(value) >= 0;
  }

  /** The index of `key`, or `-1`; `ArgumentNullException` (`key`) for `null`. */
  indexOfKey(key: K): number {
    const index = this.search(requireKey(key));

    return index >= 0 ? index : -1;
  }

  /** The first index holding `value` (by `EqualityComparer<V>.Default`), or `-1`. */
  indexOfValue(value: V): number {
    return this._values.findIndex((candidate) => elementEquals(candidate, value));
  }

  getKeyAtIndex(index: number): K {
    this.checkIndex(index);

    return at(this._keys, index);
  }

  getValueAtIndex(index: number): V {
    this.checkIndex(index);

    return at(this._values, index);
  }

  setValueAtIndex(index: number, value: V): void {
    this.checkIndex(index);
    this._values[index] = value;
    this.bumpVersion();
  }

  /** `TryGetValue`: the value, or the caller's `default(V)` on a miss (D30). */
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const index = this.indexOfKey(key);

    return index < 0 ? [false, missingValue] : [true, at(this._values, index)];
  }

  /** `GetValueOrDefault`: the value, or the caller's default on a miss (D30). */
  getValueOrDefault<D>(key: K, defaultValue: D): V | D {
    const index = this.indexOfKey(key);

    return index < 0 ? defaultValue : at(this._values, index);
  }

  copyTo(array: (readonly [K, V])[], arrayIndex = 0): void {
    copyIntoSorted([...this], array, arrayIndex);
  }

  /** .NET's ``System.Collections.Generic.SortedList`2[TKey,TValue]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.SortedList`2",
      ["TKey", "TValue"],
      this.$typeArguments$SortedList,
    );
  }

  /** `String(key)` → value, in key order. */
  toJSON(): Record<string, V> {
    const json: Record<string, V> = {};

    for (const [index, key] of this._keys.entries()) {
      json[String(key)] = at(this._values, index);
    }

    return json;
  }

  [Symbol.iterator](): Iterator<readonly [K, V]> {
    return new SortedListIterator(this, (index) => [
      at(this._keys, index),
      at(this._values, index),
    ]);
  }

  /** @internal Validates an index of the sorted arrays (`ArgumentOutOfRangeException` with the actual value). */
  checkIndex(index: number): void {
    if (index >>> 0 >= this._keys.length) {
      throwOOR(index);
    }
  }

  /**
   * .NET's binary search of `key` over the sorted keys; a comparer failure is
   * wrapped as `Array.BinarySearch` wraps it (`InvalidOperationException`,
   * `Failed to compare two elements in the array.`, the original as inner).
   */
  private search(key: K): number {
    try {
      return binarySearch(this._keys, 0, this._keys.length, key, this._compare);
    } catch (error) {
      return throwCompareFailed(error);
    }
  }

  private insertAt(index: number, key: K, value: V): void {
    this._keys.splice(index, 0, key);
    this._values.splice(index, 0, value);
    this.bumpVersion();
  }

  /** The copy constructor: sort the source by key, then reject a duplicate (by the comparer), as .NET does. */
  private copyFrom(source: Iterable<readonly [K, V]>): void {
    const entries = [...source];

    // .NET sorts the copy with `Array.Sort`, which wraps a comparer failure.
    try {
      entries.sort(([x], [y]) => this._compare(x, y));
    } catch (error) {
      throwCompareFailed(error);
    }

    for (const [index, [key, value]] of entries.entries()) {
      if (key === null || key === undefined) {
        throwArgumentNull("key");
      }

      const previous = entries[index - 1];

      if (previous !== undefined && this._compare(previous[0], key) === 0) {
        throwDuplicateKey(key);
      }

      this._keys.push(key);
      this._values.push(value);
    }
  }

  /** A view's `$typeArguments$SortedListView`: the list's key (0) or value (1) argument, when it carries them. */
  private viewTypeArguments(position: 0 | 1): readonly RuntimeType[] | undefined {
    const argument = this.$typeArguments$SortedList?.[position];

    return argument === undefined ? undefined : [argument];
  }

  private bumpVersion(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/**
 * `SortedList.Keys` / `.Values` (.NET's `KeyList` / `ValueList`): a live
 * read-only list over one of the list's arrays; its enumeration is checked
 * against the list's version.
 */
export class SortedListView<T> implements IList<T>, IReadOnlyList<T> {
  declare readonly _list: SortedListSource;
  declare readonly _isKeys: boolean;
  declare readonly $typeArguments$SortedListView: readonly RuntimeType[] | undefined;

  /** Internal: built by `SortedList.keys` / `.values` only. */
  constructor(
    list: SortedListSource,
    isKeys: boolean,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    Object.defineProperty(this, "_list", { value: list });
    Object.defineProperty(this, "_isKeys", { value: isKeys });

    Object.defineProperty(this, "$typeArguments$SortedListView", {
      value: typeArguments ?? undefined,
    });
  }

  /** .NET's `KeyList` / `ValueList`: `IList<T>` and `ICollection<T>`, no read-only interface. */
  static get $runtimeType(): CollectionType<SortedListView<unknown>> {
    return (SortedListView$rt ??= {
      kind: "collection",
      collection: "SortedListView",
      name: "SortedListView",
      fullName: "System.Collections.Generic.SortedListView",
      namespace: "System.Collections.Generic",
      ctor: SortedListView,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
      ],
      test: (value: unknown): value is SortedListView<unknown> => value instanceof SortedListView,
      closeFor: (instance) => closeCollectionFor(SortedListView.$runtimeType, instance),
    });
  }

  get count(): number {
    return this._list.count;
  }

  get length(): number {
    return this._list.count;
  }

  get isReadOnly(): true {
    return true;
  }

  get(index: number): T {
    this._list.checkIndex(index);

    return at(this.items(), index);
  }

  /** `view[^n]`. */
  fromEnd(n: number): T {
    return this.get(this.count - n);
  }

  /** Keys: the binary search (`ArgumentNullException` for `null`); values: `EqualityComparer<V>.Default`. */
  indexOf(item: T): number {
    return this._isKeys ? this._list.indexOfKey(item) : this._list.indexOfValue(item);
  }

  contains(item: T): boolean {
    return this.indexOf(item) >= 0;
  }

  /** `Array.Copy`'s checks, as .NET's `KeyList` / `ValueList.CopyTo`. */
  copyTo(array: T[], arrayIndex = 0): void {
    copyInto(this.items(), array, arrayIndex);
  }

  set(_index: number, _value: T): never {
    return this._isKeys ? throwKeyListWrite() : throwSortedListNested();
  }

  add(_item: T): never {
    return throwSortedListNested();
  }

  insert(_index: number, _item: T): never {
    return throwSortedListNested();
  }

  remove(_item: T): never {
    return throwSortedListNested();
  }

  removeAt(_index: number): never {
    return throwSortedListNested();
  }

  clear(): never {
    return throwSortedListNested();
  }

  /** Detached shallow copy (FR-055). */
  toArray(): T[] {
    return this.items().slice();
  }

  /** Zero-copy live read-only view over the list's backing key/value array (FR-056). */
  asReadOnlyJsArray(): readonly T[] {
    return this.items();
  }

  toJSON(): T[] {
    return this.toArray();
  }

  [Symbol.iterator](): Iterator<T> {
    return new SortedListIterator(this._list, (index) => at(this.items(), index));
  }

  private items(): readonly T[] {
    // A keys view's `T` is the list's `K`, a values view's its `V`: the one cast of the view.
    return (this._isKeys ? this._list._keys : this._list._values) as readonly T[];
  }
}

/** An index walk over a `SortedList`'s arrays, checked against its version on every step. */
class SortedListIterator<T> implements Iterator<T> {
  private readonly _list: SortedListSource;
  private readonly _expectedVersion: number;
  private readonly _read: (index: number) => T;
  private _index: number;

  constructor(list: SortedListSource, read: (index: number) => T) {
    this._list = list;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? list._version : 0;
    this._read = read;
    this._index = 0;
  }

  next(): IteratorResult<T> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this._list._version !== this._expectedVersion
    ) {
      throwEnumeratorModified();
    }

    if (this._index >= this._list.count) {
      return { done: true, value: undefined };
    }

    return { done: false, value: this._read(this._index++) };
  }
}

/**
 * `items[index]` for an index the caller has already checked (or that a walk
 * below `count` produced): `noUncheckedIndexedAccess` cannot see the check.
 */
function at<T>(items: readonly T[], index: number): T {
  return items[index] as T;
}
