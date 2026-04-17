import { EnumerableBase } from "./enumerable-base.ts";

/** Returns a specified number of elements from the start. */
export class TakeEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly takeCount: number,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    let taken = 0;
    for (const item of this.source) {
      if (taken >= this.takeCount) break;
      yield item;
      taken++;
    }
  }
}
