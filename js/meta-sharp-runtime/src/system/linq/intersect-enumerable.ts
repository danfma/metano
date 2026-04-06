import { EnumerableBase } from "./enumerable-base.ts";
import { HashSet } from "../collections/hash-set.ts";

/** Produces the set intersection of two sequences. Equivalent to C# .Intersect() */
export class IntersectEnumerable<T> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly second: Iterable<T>,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    const secondSet = new HashSet<T>(this.second);
    const yielded = new HashSet<T>();
    for (const item of this.source) {
      if (secondSet.has(item) && !yielded.has(item)) {
        yielded.add(item);
        yield item;
      }
    }
  }
}
