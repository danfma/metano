/**
 * `System.Linq.ILookup<TKey, TElement>`, the result of `ToLookup`: the elements grouped by
 * key, enumerated as groupings in first-seen key order. Keys are compared as `groupBy`
 * compares them.
 */
import type { HashCore } from "../collections/hash-core.ts";
import { groupingsOf } from "./grouping.ts";
import type { Grouping } from "./types.ts";

export class Lookup<K, T> implements Iterable<Grouping<K, T>> {
  declare readonly _buckets: HashCore<K, T[]>;

  constructor(buckets: HashCore<K, T[]>) {
    Object.defineProperty(this, "_buckets", { value: buckets });
  }

  /** The number of distinct keys. */
  get count(): number {
    return this._buckets.size;
  }

  /** The elements under `key`, in source order; an empty sequence when the key is absent. */
  get(key: K): Iterable<T> {
    return this._buckets.get(key) ?? [];
  }

  contains(key: K): boolean {
    return this._buckets.has(key);
  }

  [Symbol.iterator](): Iterator<Grouping<K, T>> {
    return groupingsOf(this._buckets);
  }
}
