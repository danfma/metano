/**
 * The storage of the immutable classes and its one ownership rule (spec 011
 * FR-054, research R18, decision W4-11). Internal: not re-exported by the
 * `system/collections` barrel.
 *
 * Several versions share one append-only store; each version sees only the
 * first `count` entries. A version may append in place only when its `count`
 * equals the store's length ({@link ownsTip}): it is the newest version of that
 * store, so nothing another version sees changes. Every other change, and an
 * append on an older version, copies the version's entries into a new store.
 * Nothing is ever removed from a store or overwritten in place. An old version
 * keeps the whole store alive, the entries appended after it included (a memory
 * cost, not a semantic one).
 */
import { defaultEqualityComparer, type IEqualityComparer } from "./default-comparer.ts";
import { HashCore } from "./hash-core.ts";

/** W4-11's ownership rule: a version of `count` entries may append to `store` in place. */
export function ownsTip(store: { readonly length: number }, count: number): boolean {
  return store.length === count;
}

/** `null` for the default equality comparer, so comparing comparers sees one default. */
export function normalizedComparer<T>(
  comparer: IEqualityComparer<T> | null | undefined,
): IEqualityComparer<T> | null {
  return comparer === undefined || comparer === null || comparer === defaultEqualityComparer
    ? null
    : comparer;
}

/**
 * The append-only keyed store of `ImmutableHashSet` and `ImmutableDictionary`:
 * an insertion-ordered entry array plus a `hash-core` index from each key to
 * its entry's position. A lookup for a version of `count` entries is a hit only
 * when the position it finds is below `count`, so a key another version
 * appended later stays invisible.
 */
export class ImmutableHashStorage<K, V> {
  declare readonly entries: (readonly [K, V])[];
  declare readonly index: HashCore<K, number>;

  constructor(comparer: IEqualityComparer<K> | null) {
    Object.defineProperty(this, "entries", { value: [] });
    Object.defineProperty(this, "index", { value: new HashCore<K, number>(comparer) });
  }

  /** A new store of `entries` (distinct keys by this store's comparer); the copy every non-append change makes. */
  static of<K, V>(
    comparer: IEqualityComparer<K> | null,
    entries: Iterable<readonly [K, V]>,
  ): ImmutableHashStorage<K, V> {
    const storage = new ImmutableHashStorage<K, V>(comparer);

    for (const entry of entries) {
      storage.push(entry);
    }

    return storage;
  }

  get comparer(): IEqualityComparer<K> | null {
    return this.index.comparer;
  }

  /** The position of `key` among a version's first `count` entries, or `-1`. */
  find(key: K, count: number): number {
    const position = this.index.get(key);

    return position !== undefined && position < count ? position : -1;
  }

  /**
   * The store of a version of `count` entries (which does not hold `key`) plus
   * `[key, value]`: this store, appended in place, when the version owns its tip;
   * otherwise a copy of the version's entries with the entry appended.
   */
  append(count: number, key: K, value: V): ImmutableHashStorage<K, V> {
    const target = ownsTip(this.entries, count)
      ? this
      : ImmutableHashStorage.of<K, V>(this.comparer, this.entries.slice(0, count));

    target.push([key, value]);

    return target;
  }

  /** The entry at `position`, a position `find` returned (so below the store's length). */
  entryAt(position: number): readonly [K, V] {
    return this.entries[position] as readonly [K, V];
  }

  /** A version's first `count` entries, in insertion order. */
  visible(count: number): (readonly [K, V])[] {
    return this.entries.slice(0, count);
  }

  private push(entry: readonly [K, V]): void {
    this.index.set(entry[0], this.entries.length);
    this.entries.push(entry);
  }
}
