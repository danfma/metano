import { EnumerableBase } from "./enumerable-base.ts";
import { HashSet } from "../collections/hash-set.ts";

/** Produces the set difference of two sequences. Equivalent to C# .Except() */
export class ExceptEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly second: Iterable<T>,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const excluded = new HashSet<T>(this.second);
    const yielded = new HashSet<T>();
    for (const item of this.source) {
      if (!excluded.has(item) && !yielded.has(item)) {
        yielded.add(item);
        yield item;
      }
    }
  }
}
