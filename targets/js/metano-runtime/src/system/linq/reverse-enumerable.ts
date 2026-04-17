import { EnumerableBase } from "./enumerable-base.ts";

/** Reverses the order of elements. Materializes the source. Equivalent to C# .Reverse() */
export class ReverseEnumerable<T> extends EnumerableBase<T> {
  constructor(readonly source: EnumerableBase<T>) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const items = [...this.source];
    for (let i = items.length - 1; i >= 0; i--) {
      yield items[i]!;
    }
  }
}
