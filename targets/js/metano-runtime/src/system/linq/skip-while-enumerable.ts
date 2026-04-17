import { EnumerableBase } from "./enumerable-base.ts";

/** Skips elements while a predicate is true, then yields the rest. Equivalent to C# .SkipWhile() */
export class SkipWhileEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly predicate: (item: T, index: number) => boolean,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    let skipping = true;
    let i = 0;
    for (const item of this.source) {
      if (skipping && this.predicate(item, i++)) continue;
      skipping = false;
      yield item;
    }
  }
}
