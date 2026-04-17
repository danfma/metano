import { EnumerableBase } from "./enumerable-base.ts";

/** Filters elements based on a predicate. Equivalent to C# .Where() */
export class WhereEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly predicate: (item: T, index: number) => boolean,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    let i = 0;
    for (const item of this.source) {
      if (this.predicate(item, i++)) yield item;
    }
  }
}
