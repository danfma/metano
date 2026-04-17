import { EnumerableBase } from "./enumerable-base.ts";
import { HashSet } from "../collections/hash-set.ts";

/** Produces the set union of two sequences. Equivalent to C# .Union() */
export class UnionEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly second: Iterable<T>,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const seen = new HashSet<T>();
    for (const item of this.source) {
      if (!seen.has(item)) {
        seen.add(item);
        yield item;
      }
    }
    for (const item of this.second) {
      if (!seen.has(item)) {
        seen.add(item);
        yield item;
      }
    }
  }
}
