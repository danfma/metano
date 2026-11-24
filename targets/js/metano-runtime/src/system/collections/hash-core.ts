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
  defaultEqualityComparer,
  elementEquals,
  elementHashCode,
  equalsByValue,
  type IEqualityComparer,
} from "./default-comparer.ts";

/** The result of a lookup that found no stored key (an object literal: no module-load call). */
const MISSING: Missing = { missing: true };

interface Missing {
  readonly missing: true;
}

function isMissing<K>(stored: K | Missing): stored is Missing {
  return stored === MISSING;
}

export class HashCore<K, V> {
  /**
   * `null`: `elementEquals` / `elementHashCode` with the native `Map` fast path. An explicit
   * `EqualityComparer<T>.Default` is stored as `null`, so identity keys keep that path.
   */
  declare readonly comparer: IEqualityComparer<K> | null;
  declare readonly _entries: Map<K, V>;
  declare _index: Map<number, K[]> | undefined;

  constructor(comparer?: IEqualityComparer<K> | null) {
    Object.defineProperty(this, "comparer", {
      value: comparer === defaultEqualityComparer ? null : (comparer ?? null),
    });
    Object.defineProperty(this, "_entries", { value: new Map<K, V>() });
    Object.defineProperty(this, "_index", { value: undefined, writable: true });
  }

  get size(): number {
    return this._entries.size;
  }

  /*
   * Every member takes one of two paths (spec 011 R2: one `Map` probe per operation on
   * the direct path). A key that can only equal itself (a primitive, a function, an object
   * without value equality) with no user comparer is the `Map` key itself; any other key
   * is first resolved through the hash index to the stored key equal to it.
   */

  has(key: K): boolean {
    return this._direct(key) ? this._entries.has(key) : !isMissing(this._find(key));
  }

  /** The value stored under `key`; `undefined` only on a miss (or a stored `undefined`). */
  get(key: K): V | undefined {
    if (this._direct(key)) {
      return this._entries.get(key);
    }

    const stored = this._find(key);

    return isMissing(stored) ? undefined : this._entries.get(stored);
  }

  /** The stored key equal to `key` (`HashSet.TryGetValue`), `undefined` on a miss. */
  findKey(key: K): K | undefined {
    if (this._direct(key)) {
      return this._entries.has(key) ? key : undefined;
    }

    const stored = this._find(key);

    return isMissing(stored) ? undefined : stored;
  }

  /** Stores `value` under `key`: `true` when inserted; an overwrite keeps the entry's position. */
  set(key: K, value: V): boolean {
    const entries = this._entries;

    if (this._direct(key)) {
      const size = entries.size;

      entries.set(key, value);

      return entries.size !== size;
    }

    const stored = this._find(key);

    if (!isMissing(stored)) {
      entries.set(stored, value);

      return false;
    }

    entries.set(key, value);
    this._bucketOf(this._hash(key)).push(key);

    return true;
  }

  /**
   * Appends `element` to the array stored under `key`, storing a new array when there is none:
   * a `get` and a `set` in one lookup, for the groupings of `groupBy`, `toLookup` and `join`.
   */
  append<E>(this: HashCore<K, E[]>, key: K, element: E): void {
    const entries = this._entries;

    if (this._direct(key)) {
      const bucket = entries.get(key);

      if (bucket === undefined) {
        entries.set(key, [element]);
      } else {
        bucket.push(element);
      }

      return;
    }

    const stored = this._find(key);

    if (!isMissing(stored)) {
      entries.get(stored)!.push(element);

      return;
    }

    entries.set(key, [element]);
    this._bucketOf(this._hash(key)).push(key);
  }

  delete(key: K): boolean {
    if (this._direct(key)) {
      return this._entries.delete(key);
    }

    const stored = this._find(key);

    if (isMissing(stored)) {
      return false;
    }

    this._entries.delete(stored);
    this._unindex(stored);

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

  /** Whether `key` is its own `Map` key: no user comparer, and identity is its equality. */
  private _direct(key: K): boolean {
    return (
      this.comparer === null && (typeof key !== "object" || key === null || !equalsByValue(key))
    );
  }

  /** The stored key the index holds for `key`, or `MISSING`. */
  private _find(key: K): K | Missing {
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
