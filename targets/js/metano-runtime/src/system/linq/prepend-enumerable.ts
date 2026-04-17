import { EnumerableBase } from "./enumerable-base.ts";

/** Prepends a single element to the beginning of a sequence. Equivalent to C# .Prepend() */
export class PrependEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly element: T,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    yield this.element;
    yield* this.source;
  }
}
