import { EnumerableBase } from "./enumerable-base.ts";

/** Yields elements while a predicate is true. Equivalent to C# .TakeWhile() */
export class TakeWhileEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly predicate: (item: T, index: number) => boolean,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    let i = 0;
    for (const item of this.source) {
      if (!this.predicate(item, i++)) break;
      yield item;
    }
  }
}
