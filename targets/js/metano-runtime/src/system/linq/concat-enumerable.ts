import { EnumerableBase } from "./enumerable-base.ts";

/** Concatenates two sequences. */
export class ConcatEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly firstSource: EnumerableBase<T>,
    readonly secondSource: Iterable<T>,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    yield* this.firstSource;
    yield* this.secondSource;
  }
}
