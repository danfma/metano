/**
 * The keyed store shared by `Dictionary`, `HashSet` and (stage 011h)
 * `ConcurrentDictionary` and the hashed immutable classes (spec 011 D24,
 * contracts/runtime-collections.md § `hash-core.ts`). Internal: not re-exported
 * by the collections barrel.
 *
 * Entries live in one native `Map` keyed by each entry's stored key, so
 * enumeration is in insertion order and tolerates mutation the way a `Map`
 * does. A key whose equality is identity (a primitive, an object without
 * `equals`) is the `Map` key itself; a key that can equal another object (a
 * record, a box, a `Decimal`, a Temporal value) is found through a hash index
 * of stored keys first. With a user comparer every key goes through the index.
 * The core validates nothing and keeps no version: its owners do both.
 */
import {
  elementEquals,
  elementHashCode,
  equalsByValue,
  type IEqualityComparer,
} from "./default-comparer.ts";

/** The result of a lookup that found no stored key (an object literal: no module-load call). */
const MISSING: { readonly missing: true } = { missing: true };

type Lookup<K> = K | typeof MISSING;

export class HashCore<K, V> {
  /** `null`: `elementEquals` / `elementHashCode` with the native `Map` fast path. */
  declare readonly comparer: IEqualityComparer<K> | null;
  declare readonly _entries: Map<K, V>;
  declare _index: Map<number, K[]> | undefined;

  constructor(comparer?: IEqualityComparer<K> | null) {
    Object.defineProperty(this, "comparer", { value: comparer ?? null });
    Object.defineProperty(this, "_entries", { value: new Map<K, V>() });
    Object.defineProperty(this, "_index", { value: undefined, writable: true });
  }

  get size(): number {
    return this._entries.size;
  }

  has(key: K): boolean {
    return this._stored(key) !== MISSING;
  }

  /** The value stored under `key`; `undefined` only on a miss (or a stored `undefined`). */
  get(key: K): V | undefined {
    const stored = this._stored(key);

    return stored === MISSING ? undefined : this._entries.get(stored as K);
  }

  /** The stored key equal to `key` (`HashSet.TryGetValue`), `undefined` on a miss. */
  findKey(key: K): K | undefined {
    const stored = this._stored(key);

    return stored === MISSING ? undefined : (stored as K);
  }

  /** Stores `value` under `key`: `true` when inserted; an overwrite keeps the entry's position. */
  set(key: K, value: V): boolean {
    const stored = this._stored(key);

    if (stored !== MISSING) {
      this._entries.set(stored as K, value);

      return false;
    }

    this._entries.set(key, value);

    if (this._indexes(key)) {
      this._bucketOf(this._hash(key)).push(key);
    }

    return true;
  }

  delete(key: K): boolean {
    const stored = this._stored(key);

    if (stored === MISSING) {
      return false;
    }

    this._entries.delete(stored as K);

    if (this._indexes(stored as K)) {
      this._unindex(stored as K);
    }

    return true;
  }

  clear(): void {
    this._entries.clear();
    this._index = undefined;
  }

  /** An O(n) copy: same comparer, same enumeration order. */
  clone(): HashCore<K, V> {
    const copy = new HashCore<K, V>(this.comparer);

    for (const [key, value] of this._entries) {
      copy._entries.set(key, value);
    }

    if (this._index !== undefined) {
      copy._index = new Map(Array.from(this._index, ([hash, keys]) => [hash, keys.slice()]));
    }

    return copy;
  }

  /** `[key, value]` in insertion order; never throws while the core changes. */
  [Symbol.iterator](): Iterator<[K, V]> {
    return this._entries.entries();
  }

  /** The stored keys, in insertion order. */
  keys(): IterableIterator<K> {
    return this._entries.keys();
  }

  /** The stored values, in insertion order. */
  values(): IterableIterator<V> {
    return this._entries.values();
  }

  private _stored(key: K): Lookup<K> {
    if (!this._indexes(key)) {
      return this._entries.has(key) ? key : MISSING;
    }

    const bucket = this._index?.get(this._hash(key));

    if (bucket === undefined) {
      return MISSING;
    }

    for (const stored of bucket) {
      if (this._equals(stored, key)) {
        return stored;
      }
    }

    return MISSING;
  }

  private _indexes(key: K): boolean {
    return this.comparer !== null || equalsByValue(key);
  }

  private _hash(key: K): number {
    return this.comparer === null ? elementHashCode(key) : this.comparer.getHashCode(key);
  }

  private _equals(stored: K, key: K): boolean {
    return this.comparer === null ? elementEquals(stored, key) : this.comparer.equals(stored, key);
  }

  private _bucketOf(hash: number): K[] {
    this._index ??= new Map();

    const known = this._index.get(hash);

    if (known !== undefined) {
      return known;
    }

    const created: K[] = [];

    this._index.set(hash, created);

    return created;
  }

  private _unindex(stored: K): void {
    const hash = this._hash(stored);
    const bucket = this._index?.get(hash);

    if (bucket === undefined) {
      return;
    }

    const position = bucket.indexOf(stored);

    if (position >= 0) {
      bucket.splice(position, 1);
    }

    if (bucket.length === 0) {
      this._index?.delete(hash);
    }
  }
}
