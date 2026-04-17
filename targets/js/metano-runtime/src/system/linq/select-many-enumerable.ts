import { EnumerableBase } from "./enumerable-base.ts";

/** Projects each element to an iterable and flattens. Equivalent to C# .SelectMany() */
export class SelectManyEnumerable<T, R> extends EnumerableBase<R> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly selector: (item: T, index: number) => Iterable<R>,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<R> {
    let i = 0;
    for (const item of this.source) {
      yield* this.selector(item, i++);
    }
  }
}
