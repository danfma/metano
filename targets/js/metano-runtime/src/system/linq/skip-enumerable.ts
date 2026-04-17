import { EnumerableBase } from "./enumerable-base.ts";

/** Skips a specified number of elements from the start. */
export class SkipEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly skipCount: number,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    let skipped = 0;
    for (const item of this.source) {
      if (skipped < this.skipCount) {
        skipped++;
        continue;
      }
      yield item;
    }
  }
}
