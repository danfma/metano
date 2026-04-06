import { EnumerableBase } from "./enumerable-base.ts";
import { HashSet } from "../collections/hash-set.ts";

/** Returns distinct elements using HashSet-based deduplication (respects equals/hashCode). */
export class DistinctEnumerable<T> extends EnumerableBase<T> {
  constructor(readonly source: EnumerableBase<T>) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const seen = new HashSet<T>();
    for (const item of this.source) {
      if (!seen.has(item)) {
        seen.add(item);
        yield item;
      }
    }
  }
}
