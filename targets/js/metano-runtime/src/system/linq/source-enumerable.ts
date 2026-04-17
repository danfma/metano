import { EnumerableBase } from "./enumerable-base.ts";

/** Wraps an existing Iterable as the source of a LINQ chain. */
export class SourceEnumerable<T> extends EnumerableBase<T> {
  constructor(readonly source: Iterable<T>) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    yield* this.source;
  }
}
