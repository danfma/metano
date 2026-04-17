import { EnumerableBase } from "./enumerable-base.ts";

/** Returns distinct elements by a key selector. Equivalent to C# .DistinctBy() */
export class DistinctByEnumerable<T, K> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly keySelector: (item: T) => K,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const seen = new Set<K>();
    for (const item of this.source) {
      const key = this.keySelector(item);
      if (!seen.has(key)) {
        seen.add(key);
        yield item;
      }
    }
  }
}
