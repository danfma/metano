import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { elementEquals, type IEqualityComparer } from "./default-comparer.ts";
import { HashCore } from "./hash-core.ts";
import type { IDictionary, IReadOnlyDictionary } from "./interfaces.ts";
import { ReadOnlyList } from "./read-only-list.ts";
import {
  requireKey,
  throwArgumentNull,
  throwConcurrencyLevel,
  throwConcurrentDuplicateKey,
  throwConcurrentSourceDuplicate,
  throwKeyNotFound,
  throwNegativeValue,
} from "./throw-helpers.ts";

let ConcurrentDictionary$rt: CollectionType<ConcurrentDictionary<unknown, unknown>> | undefined;

/**
 * Mirrors `System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>`
 * (spec 011 FR-054): a sibling of `Dictionary` over the same `hash-core`
 * (`instanceof Dictionary` is `false`, as `is Dictionary<K, V>` in .NET). Each
 * call runs to completion without yielding, so it is atomic: JavaScript shares
 * no object between threads. The enumerator never throws while the dictionary
 * changes (it walks the live store, as .NET's does), and `keys` / `values` are
 * snapshots.
 */
export class ConcurrentDictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  declare readonly _store: HashCore<K, V>;
  declare readonly $typeArguments$ConcurrentDictionary: readonly RuntimeType[] | undefined;

  /** Hand-written TS: `entries` must not repeat a key (`ArgumentException`, as .NET's copy constructor). */
  constructor(
    entries?: Iterable<readonly [K, V]> | null,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    Object.defineProperty(this, "_store", { value: new HashCore<K, V>(comparer) });
    Object.defineProperty(this, "$typeArguments$ConcurrentDictionary", {
      value: typeArguments ?? undefined,
    });

    if (entries !== null && entries !== undefined) {
      for (const [key, value] of entries) {
        if (!this.tryAdd(key, value)) {
          throwConcurrentSourceDuplicate();
        }
      }
    }
  }

  /** `new ConcurrentDictionary<K, V>(collection[, comparer])`: `ArgumentNullException` (`collection`) for `null`. */
  static from<K, V>(
    entries: Iterable<readonly [K, V]> | null | undefined,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ConcurrentDictionary<K, V> {
    if (entries === null || entries === undefined) {
      throwArgumentNull("collection");
    }

    return new ConcurrentDictionary(entries, comparer, typeArguments);
  }

  /** `new ConcurrentDictionary<K, V>(concurrencyLevel, capacity[, comparer])`: both validated, then dropped. */
  static withCapacity<K, V>(
    concurrencyLevel: number,
    capacity: number,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ConcurrentDictionary<K, V> {
    if (concurrencyLevel < 1 && concurrencyLevel !== -1) {
      throwConcurrencyLevel();
    }

    if (capacity < 0) {
      throwNegativeValue("capacity", capacity);
    }

    return new ConcurrentDictionary(null, comparer, typeArguments);
  }

  /** The `{ [k] = v }` initializer (and JSON): indexer semantics, the last value wins. */
  static assign<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ConcurrentDictionary<K, V> {
    const dictionary = new ConcurrentDictionary<K, V>(null, comparer, typeArguments);

    for (const [key, value] of entries) {
      dictionary.set(key, value);
    }

    return dictionary;
  }

  static get $runtimeType(): CollectionType<ConcurrentDictionary<unknown, unknown>> {
    ConcurrentDictionary$rt ??= {
      kind: "collection",
      collection: "ConcurrentDictionary",
      name: "ConcurrentDictionary",
      fullName: "System.Collections.Concurrent.ConcurrentDictionary",
      namespace: "System.Collections.Concurrent",
      ctor: ConcurrentDictionary,
      typeArguments: [],
      variance: ["invariant", "invariant"],
      interfaces: [
        { collection: "IDictionary", arguments: [0, 1] },
        { collection: "IReadOnlyDictionary", arguments: [0, 1] },
        { collection: "ICollection", arguments: null },
        { collection: "IReadOnlyCollection", arguments: null },
      ],
      test: (value: unknown): value is ConcurrentDictionary<unknown, unknown> =>
        value instanceof ConcurrentDictionary,
      closeFor: (instance) => closeCollectionFor(ConcurrentDictionary.$runtimeType, instance),
    };

    return ConcurrentDictionary$rt;
  }

  get count(): number {
    return this._store.size;
  }

  get isEmpty(): boolean {
    return this._store.size === 0;
  }

  get isReadOnly(): boolean {
    return false;
  }

  /** `Keys`: a snapshot (.NET returns a new `ReadOnlyCollection<TKey>`). */
  get keys(): ReadOnlyList<K> {
    return new ReadOnlyList([...this._store.keys()]);
  }

  /** `Values`: a snapshot (.NET returns a new `ReadOnlyCollection<TValue>`). */
  get values(): ReadOnlyList<V> {
    return new ReadOnlyList([...this._store.values()]);
  }

  get(key: K): V {
    const store = this._store;

    if (!store.has(requireKey(key))) {
      throwKeyNotFound(key);
    }

    return storedValue(store, key);
  }

  set(key: K, value: V): void {
    this._store.set(requireKey(key), value);
  }

  /** `dict[k] op= …`: read (`KeyNotFoundException` on a miss), apply, write; returns the new value. */
  update(key: K, f: (current: V) => V): V {
    const value = f(this.get(key));

    this.set(key, value);

    return value;
  }

  /** `dict[k]++` in expression position: as `update`, returning the old value. */
  updateReturningOld(key: K, f: (current: V) => V): V {
    const current = this.get(key);

    this.set(key, f(current));

    return current;
  }

  /** `IDictionary<K, V>.Add`: `ArgumentException` (`The key already existed in the dictionary.`) on a duplicate. */
  add(key: K, value: V): void {
    if (!this.tryAdd(key, value)) {
      throwConcurrentDuplicateKey();
    }
  }

  remove(key: K): boolean {
    return this._store.delete(requireKey(key));
  }

  clear(): void {
    this._store.clear();
  }

  containsKey(key: K): boolean {
    return this._store.has(requireKey(key));
  }

  /** `TryGetValue`: the value, or the caller's `default(V)` on a miss (D30). */
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const store = this._store;

    return store.has(requireKey(key)) ? [true, storedValue(store, key)] : [false, missingValue];
  }

  /** `GetValueOrDefault`: the value, or the caller's default on a miss (D30). */
  getValueOrDefault<D>(key: K, defaultValue: D): V | D {
    return this.tryGetValue(key, defaultValue)[1];
  }

  /** `true` when `key` was absent and is now stored. */
  tryAdd(key: K, value: V): boolean {
    const store = this._store;

    if (store.has(requireKey(key))) {
      return false;
    }

    store.set(key, value);

    return true;
  }

  /** Stores `newValue` when the current value equals `comparisonValue` (`EqualityComparer<V>.Default`). */
  tryUpdate(key: K, newValue: V, comparisonValue: V): boolean {
    const store = this._store;

    if (!store.has(requireKey(key)) || !elementEquals(store.get(key), comparisonValue)) {
      return false;
    }

    store.set(key, newValue);

    return true;
  }

  /** `TryRemove(key, out value)`: the removed value, or the caller's `default(V)` on a miss (D30). */
  tryRemove<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const store = this._store;

    if (!store.has(requireKey(key))) {
      return [false, missingValue];
    }

    const value = storedValue(store, key);

    store.delete(key);

    return [true, value];
  }

  /** `GetOrAdd(key, value)`: the stored value, inserting `value` first when the key is absent. */
  getOrAdd(key: K, value: V): V {
    const store = this._store;

    if (store.has(requireKey(key))) {
      return storedValue(store, key);
    }

    store.set(key, value);

    return value;
  }

  /**
   * `GetOrAdd(key, valueFactory[, factoryArgument])`: as .NET, the factory runs
   * only when the key is absent, and a value the factory stored itself wins.
   */
  getOrAddWith<A = undefined>(
    key: K,
    valueFactory: (key: K, factoryArgument: A) => V,
    factoryArgument?: A,
  ): V {
    const store = this._store;

    requireKey(key);

    if (typeof valueFactory !== "function") {
      throwArgumentNull("valueFactory");
    }

    if (store.has(key)) {
      return storedValue(store, key);
    }

    // An omitted factory argument reaches the factory as `undefined`, which only the
    // one-argument C# overload produces; its factory never reads it.
    return this.getOrAdd(key, valueFactory(key, factoryArgument as A));
  }

  /** `AddOrUpdate(key, addValue, updateValueFactory)`: the value stored. */
  addOrUpdate(key: K, addValue: V, updateValueFactory: (key: K, current: V) => V): V {
    requireKey(key);

    if (typeof updateValueFactory !== "function") {
      throwArgumentNull("updateValueFactory");
    }

    return this.addOrUpdateLoop(
      key,
      () => addValue,
      (current) => updateValueFactory(key, current),
    );
  }

  /** `AddOrUpdate(key, addValueFactory, updateValueFactory[, factoryArgument])`: the value stored. */
  addOrUpdateWith<A = undefined>(
    key: K,
    addValueFactory: (key: K, factoryArgument: A) => V,
    updateValueFactory: (key: K, current: V, factoryArgument: A) => V,
    factoryArgument?: A,
  ): V {
    requireKey(key);

    if (typeof addValueFactory !== "function") {
      throwArgumentNull("addValueFactory");
    }

    if (typeof updateValueFactory !== "function") {
      throwArgumentNull("updateValueFactory");
    }

    // As in getOrAddWith: an omitted factory argument is the one-argument overload's.
    return this.addOrUpdateLoop(
      key,
      () => addValueFactory(key, factoryArgument as A),
      (current) => updateValueFactory(key, current, factoryArgument as A),
    );
  }

  /** A `[K, V]` snapshot. */
  toArray(): [K, V][] {
    return [...this._store];
  }

  /** `String(key)` → value. */
  toJSON(): Record<string, V> {
    const json: Record<string, V> = {};

    for (const [key, value] of this._store) {
      json[String(key)] = value;
    }

    return json;
  }

  /** .NET's ``System.Collections.Concurrent.ConcurrentDictionary`2[TKey,TValue]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Concurrent.ConcurrentDictionary`2",
      ["TKey", "TValue"],
      this.$typeArguments$ConcurrentDictionary,
    );
  }

  /** The live store in insertion order; never throws while the dictionary changes. */
  [Symbol.iterator](): Iterator<readonly [K, V]> {
    return this._store[Symbol.iterator]();
  }

  /**
   * .NET's retry loop: an update stores its value only if the current value is
   * still the one it read, and an add only if the key is still absent, so a
   * factory that changes the same key makes the call run again.
   */
  private addOrUpdateLoop(key: K, add: () => V, update: (current: V) => V): V {
    const store = this._store;

    for (;;) {
      if (store.has(key)) {
        const current = storedValue(store, key);
        const next = update(current);

        if (this.tryUpdate(key, next, current)) {
          return next;
        }

        continue;
      }

      const added = add();

      if (this.tryAdd(key, added)) {
        return added;
      }
    }
  }
}

/** The value under `key`, read after `store.has(key)`: a hit, so never the miss `undefined`. */
function storedValue<K, V>(store: HashCore<K, V>, key: K): V {
  return store.get(key) as V;
}
