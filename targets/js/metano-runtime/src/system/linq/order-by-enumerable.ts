import { EnumerableBase } from "./enumerable-base.ts";
import { compareKeys } from "./compare-keys.ts";

/** Sorts elements by a key. Materializes the source on first iteration. */
export class OrderByEnumerable<T, K> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly keySelector: (item: T) => K,
    readonly descending: boolean,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const items = [...this.source];
    items.sort((a, b) => {
      const cmp = compareKeys(this.keySelector(a), this.keySelector(b));
      return this.descending ? -cmp : cmp;
    });
    yield* items;
  }
}
