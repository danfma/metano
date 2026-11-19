/**
 * `System.Collections.Generic.Dictionary<TKey, TValue>` (spec 011 FR-001,
 * FR-006, FR-012, D18, D30; contracts/runtime-collections.md § `Dictionary<K, V>`).
 * Keys go through `elementEquals` / `elementHashCode` or a user
 * `IEqualityComparer<K>` over the shared `hash-core`; entries enumerate in
 * insertion order. Only the insertion of a new key invalidates a running
 * enumeration (.NET Core 3.0+: `remove`, `clear` and overwrites do not).
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { elementEquals, type IEqualityComparer } from "./default-comparer.ts";
import { KeyCollection, ValueCollection } from "./dictionary-views.ts";
import { HashCore } from "./hash-core.ts";
import type { IDictionary, IReadOnlyDictionary } from "./interfaces.ts";
import {
  throwArgumentNull,
  throwDuplicateKey,
  throwKeyNotFound,
  throwModified,
  throwNegative,
  requireKey,
} from "./throw-helpers.ts";

let Dictionary$rt: CollectionType<Dictionary<unknown, unknown>> | undefined;

export class Dictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  declare readonly _store: HashCore<K, V>;
  declare _version: number;
  declare _keys: KeyCollection<K> | undefined;
  declare _values: ValueCollection<V> | undefined;
  declare readonly $typeArguments$Dictionary: readonly RuntimeType[] | undefined;

  /** Hand-written TS: `entries` are added (a duplicate key throws). */
  constructor(
    entries?: Iterable<readonly [K, V]> | null,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    Object.defineProperty(this, "_store", { value: new HashCore<K, V>(comparer) });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "_keys", { value: undefined, writable: true });
    Object.defineProperty(this, "_values", { value: undefined, writable: true });
    Object.defineProperty(this, "$typeArguments$Dictionary", {
      value: typeArguments ?? undefined,
    });

    if (entries !== null && entries !== undefined) {
      this.initAdd(entries);
    }
  }

  /** `{ { k, v } }`: add semantics, a duplicate key throws. */
  static of<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): Dictionary<K, V> {
    return new Dictionary<K, V>(entries, comparer, typeArguments);
  }

  /** `{ [k] = v }`: indexer semantics, the last value for a key wins. */
  static assign<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): Dictionary<K, V> {
    return new Dictionary<K, V>(null, comparer, typeArguments).initAssign(entries);
  }

  /** The copy constructors (`IDictionary` or `IEnumerable<KeyValuePair>` source). */
  static from<K, V>(
    source: Iterable<readonly [K, V]>,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): Dictionary<K, V> {
    if (source === null || source === undefined) {
      throwArgumentNull("collection");
    }

    return new Dictionary<K, V>(source, comparer, typeArguments);
  }

  /** `new Dictionary<K, V>(capacity[, comparer])`: the capacity is evaluated and validated once. */
  static withCapacity<K, V>(
    capacity: number,
    comparer?: IEqualityComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): Dictionary<K, V> {
    if (capacity < 0) {
      throwNegative("capacity");
    }

    return new Dictionary<K, V>(null, comparer, typeArguments);
  }

  /** The runtime type descriptor of the open `Dictionary<K, V>`: a data literal, built on first read. */
  static get $runtimeType(): CollectionType<Dictionary<unknown, unknown>> {
    Dictionary$rt ??= {
      kind: "collection",
      collection: "Dictionary",
      name: "Dictionary",
      fullName: "System.Collections.Generic.Dictionary",
      namespace: "System.Collections.Generic",
      ctor: Dictionary,
      typeArguments: [],
      variance: ["invariant", "invariant"],
      interfaces: [
        { collection: "IDictionary", arguments: [0, 1] },
        { collection: "IReadOnlyDictionary", arguments: [0, 1] },
        { collection: "ICollection", arguments: null },
        { collection: "IReadOnlyCollection", arguments: null },
      ],
      test: (value: unknown): value is Dictionary<unknown, unknown> => value instanceof Dictionary,
      closeFor: (instance) => closeCollectionFor(Dictionary.$runtimeType, instance),
    };

    return Dictionary$rt;
  }

  get count(): number {
    return this._store.size;
  }

  get isReadOnly(): boolean {
    return false;
  }

  /** A live view of the keys, created once (`dict.keys === dict.keys`, as on .NET). */
  get keys(): KeyCollection<K> {
    this._keys ??= new KeyCollection<K>(this);

    return this._keys;
  }

  /** A live view of the values, created once. */
  get values(): ValueCollection<V> {
    this._values ??= new ValueCollection<V>(this);

    return this._values;
  }

  get(key: K): V {
    const store = this._store;

    if (!store.has(requireKey(key))) {
      throwKeyNotFound(key);
    }

    return store.get(key) as V;
  }

  /** The indexer write: inserts a new key (invalidating enumerations) or overwrites. */
  set(key: K, value: V): void {
    if (this._store.set(requireKey(key), value)) {
      this._bump();
    }
  }

  /** `dict[k] op= …` (closure form): `KeyNotFoundException` on a miss; returns the new value. */
  update(key: K, f: (current: V) => V): V {
    const next = f(this.get(key));

    this.set(key, next);

    return next;
  }

  /** `dict[k]++` whose value is used: like {@link update}, returning the old value. */
  updateReturningOld(key: K, f: (current: V) => V): V {
    const old = this.get(key);

    this.set(key, f(old));

    return old;
  }

  add(key: K, value: V): void {
    if (!this.tryAdd(key, value)) {
      throwDuplicateKey(key);
    }
  }

  tryAdd(key: K, value: V): boolean {
    const store = this._store;

    if (store.has(requireKey(key))) {
      return false;
    }

    store.set(key, value);
    this._bump();

    return true;
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

  containsValue(value: V): boolean {
    for (const stored of this._store.values()) {
      if (elementEquals(stored, value)) {
        return true;
      }
    }

    return false;
  }

  /** `TryGetValue(key, out value)`: `missingValue` is what C# stores on a miss. */
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const store = this._store;

    return store.has(requireKey(key)) ? [true, store.get(key) as V] : [false, missingValue];
  }

  getValueOrDefault(key: K): V | null;
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  getValueOrDefault(key: K, defaultValue: unknown = null): unknown {
    return this.tryGetValue(key, defaultValue)[1];
  }

  /** An interop copy as a native `Map`. */
  toMap(): Map<K, V> {
    return new Map(this._store);
  }

  /** Initializer entries after a capacity / copy / comparer constructor: add semantics, chainable. */
  initAdd(entries: Iterable<readonly [K, V]>): this {
    for (const [key, value] of entries) {
      this.add(key, value);
    }

    return this;
  }

  /** `{ [k] = v }` entries after such a constructor: set semantics, chainable. */
  initAssign(entries: Iterable<readonly [K, V]>): this {
    for (const [key, value] of entries) {
      this.set(key, value);
    }

    return this;
  }

  /** `String(key)` → value, as `JSON.stringify` writes an object. */
  toJSON(): Record<string, V> {
    const result: Record<string, V> = {};

    for (const [key, value] of this._store) {
      result[String(key)] = value;
    }

    return result;
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.Dictionary`2",
      ["TKey", "TValue"],
      this.$typeArguments$Dictionary,
    );
  }

  /** `[key, value]` in insertion order; a fresh tuple per step. */
  [Symbol.iterator](): IterableIterator<readonly [K, V]> {
    return new DictionaryIterator(this);
  }

  private _bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `Dictionary`'s enumerator: an insertion makes the next step throw. */
class DictionaryIterator<K, V> implements IterableIterator<readonly [K, V]> {
  private readonly _dictionary: Dictionary<K, V>;
  private readonly _expectedVersion: number;
  private readonly _entries: Iterator<[K, V]>;

  constructor(dictionary: Dictionary<K, V>) {
    this._dictionary = dictionary;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? dictionary._version : 0;
    this._entries = dictionary._store[Symbol.iterator]();
  }

  next(): IteratorResult<readonly [K, V]> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this._dictionary._version !== this._expectedVersion
    ) {
      throwModified();
    }

    return this._entries.next();
  }

  [Symbol.iterator](): IterableIterator<readonly [K, V]> {
    return this;
  }
}
