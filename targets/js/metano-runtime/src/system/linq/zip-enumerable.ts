import { EnumerableBase } from "./enumerable-base.ts";

/** Combines two sequences element-by-element using a result selector. Equivalent to C# .Zip() */
export class ZipEnumerable<T, U, R> extends EnumerableBase<R> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly second: Iterable<U>,
    readonly resultSelector: (first: T, second: U) => R,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<R> {
    const iter1 = this.source[Symbol.iterator]();
    const iter2 = this.second[Symbol.iterator]();
    while (true) {
      const a = iter1.next();
      const b = iter2.next();
      if (a.done || b.done) break;
      yield this.resultSelector(a.value, b.value);
    }
  }
}
