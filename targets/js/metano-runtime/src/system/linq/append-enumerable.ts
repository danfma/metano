import { EnumerableBase } from "./enumerable-base.ts";

/** Appends a single element to the end of a sequence. Equivalent to C# .Append() */
export class AppendEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly element: T,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    yield* this.source;
    yield this.element;
  }
}
