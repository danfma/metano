import { type Grouping, EnumerableBase } from "./enumerable-base.ts";

/** Groups elements by a key selector. Materializes on first iteration. */
export class GroupByEnumerable<T, K> extends EnumerableBase<Grouping<K, T>> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly keySelector: (item: T) => K,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<Grouping<K, T>> {
    const groups = new Map<K, T[]>();
    for (const item of this.source) {
      const key = this.keySelector(item);
      let group = groups.get(key);
      if (!group) {
        group = [];
        groups.set(key, group);
      }
      group.push(item);
    }

    for (const [key, items] of groups) {
      yield {
        key,
        [Symbol.iterator]() {
          return items[Symbol.iterator]();
        },
      };
    }
  }
}
