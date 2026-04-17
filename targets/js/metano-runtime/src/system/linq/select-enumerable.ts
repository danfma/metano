import { EnumerableBase } from "./enumerable-base.ts";

/** Projects each element into a new form. Equivalent to C# .Select() */
export class SelectEnumerable<T, R> extends EnumerableBase<R> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly selector: (item: T, index: number) => R,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<R> {
    let i = 0;
    for (const item of this.source) {
      yield this.selector(item, i++);
    }
  }
}
