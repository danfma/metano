/**
 * Composition operator factories — each returns a tagged descriptor.
 *
 * `apply` walks the source via a generator wrapped behind
 * `[Symbol.iterator]` so each consumer creates a fresh iterator
 * (re-enumerable like .NET LINQ). The descriptor carries the raw
 * lambdas verbatim so an IQueryable provider can introspect the chain
 * without parsing closures.
 *
 * Lazy semantics: composition allocates only the descriptor; the
 * generator runs on `[Symbol.iterator]()` from the consumer side.
 * Short-circuit terminals (`first`, `take(n)`) stop iterating as soon
 * as their result is determined.
 */
import { compareKeys } from "./compare-keys.ts";
import type { QueryableMeta } from "./expr-tree.ts";
import type {
  AppendOp,
  ConcatOp,
  DistinctByOp,
  DistinctOp,
  OrderByDescendingOp,
  OrderByOp,
  PrependOp,
  ReverseOp,
  SelectManyOp,
  SelectOp,
  SkipOp,
  SkipWhileOp,
  TakeOp,
  TakeWhileOp,
  ThenByDescendingOp,
  ThenByOp,
  WhereOp,
  ZipOp,
} from "./types.ts";

/**
 * Internal Iterable carrier capturing the chain of comparers built by
 * `orderBy` / `thenBy` / `thenByDescending`. Sort runs lazily on
 * enumeration so composite multi-key sorts collapse to a single
 * `Array.sort` with a chained comparator that respects key declaration
 * order.
 */
type Comparer<T> = (a: T, b: T) => number;

class OrderedIter<T> implements Iterable<T> {
  constructor(
    readonly source: Iterable<T>,
    readonly comparers: readonly Comparer<T>[],
  ) {}

  *[Symbol.iterator](): Iterator<T> {
    const arr = [...this.source];
    arr.sort((a, b) => {
      for (const cmp of this.comparers) {
        const r = cmp(a, b);
        if (r !== 0) return r;
      }
      return 0;
    });
    yield* arr;
  }
}

function ascendingComparer<T, K>(keySelector: (item: T) => K): Comparer<T> {
  return (a, b) => compareKeys(keySelector(a), keySelector(b));
}

function descendingComparer<T, K>(keySelector: (item: T) => K): Comparer<T> {
  return (a, b) => -compareKeys(keySelector(a), keySelector(b));
}

export function where<T>(
  predicate: (item: T, index: number) => boolean,
  queryable?: QueryableMeta,
): WhereOp<T> {
  return {
    kind: "where",
    predicate,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let i = 0;
        for (const item of source) if (predicate(item, i++)) yield item;
      },
    }),
  };
}

export function select<T, R>(
  selector: (item: T, index: number) => R,
  queryable?: QueryableMeta,
): SelectOp<T, R> {
  return {
    kind: "select",
    selector,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let i = 0;
        for (const item of source) yield selector(item, i++);
      },
    }),
  };
}

export function selectMany<T, R>(
  selector: (item: T, index: number) => Iterable<R>,
  queryable?: QueryableMeta,
): SelectManyOp<T, R> {
  return {
    kind: "selectMany",
    selector,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let i = 0;
        for (const item of source) for (const inner of selector(item, i++)) yield inner;
      },
    }),
  };
}

export function take<T>(count: number): TakeOp<T> {
  return {
    kind: "take",
    count,
    apply: (source) => ({
      *[Symbol.iterator]() {
        if (count <= 0) return;
        let n = 0;
        for (const item of source) {
          yield item;
          if (++n >= count) return;
        }
      },
    }),
  };
}

export function skip<T>(count: number): SkipOp<T> {
  return {
    kind: "skip",
    count,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let n = 0;
        for (const item of source) {
          if (n++ < count) continue;
          yield item;
        }
      },
    }),
  };
}

export function takeWhile<T>(
  predicate: (item: T, index: number) => boolean,
  queryable?: QueryableMeta,
): TakeWhileOp<T> {
  return {
    kind: "takeWhile",
    predicate,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let i = 0;
        for (const item of source) {
          if (!predicate(item, i++)) return;
          yield item;
        }
      },
    }),
  };
}

export function skipWhile<T>(
  predicate: (item: T, index: number) => boolean,
  queryable?: QueryableMeta,
): SkipWhileOp<T> {
  return {
    kind: "skipWhile",
    predicate,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        let i = 0;
        let skipping = true;
        for (const item of source) {
          if (skipping && predicate(item, i++)) continue;
          skipping = false;
          yield item;
        }
      },
    }),
  };
}

export function distinct<T>(): DistinctOp<T> {
  return {
    kind: "distinct",
    apply: (source) => ({
      *[Symbol.iterator]() {
        const seen = new Set<T>();
        for (const item of source)
          if (!seen.has(item)) {
            seen.add(item);
            yield item;
          }
      },
    }),
  };
}

export function distinctBy<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): DistinctByOp<T, K> {
  return {
    kind: "distinctBy",
    keySelector,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        const seen = new Set<K>();
        for (const item of source) {
          const key = keySelector(item);
          if (!seen.has(key)) {
            seen.add(key);
            yield item;
          }
        }
      },
    }),
  };
}

export function concat<T>(other: Iterable<T>): ConcatOp<T> {
  return {
    kind: "concat",
    other,
    apply: (source) => ({
      *[Symbol.iterator]() {
        for (const item of source) yield item;
        for (const item of other) yield item;
      },
    }),
  };
}

export function append<T>(element: T): AppendOp<T> {
  return {
    kind: "append",
    element,
    apply: (source) => ({
      *[Symbol.iterator]() {
        for (const item of source) yield item;
        yield element;
      },
    }),
  };
}

export function prepend<T>(element: T): PrependOp<T> {
  return {
    kind: "prepend",
    element,
    apply: (source) => ({
      *[Symbol.iterator]() {
        yield element;
        for (const item of source) yield item;
      },
    }),
  };
}

export function reverse<T>(): ReverseOp<T> {
  return {
    kind: "reverse",
    apply: (source) => ({
      *[Symbol.iterator]() {
        const buffer = [...source];
        for (let i = buffer.length - 1; i >= 0; i--) yield buffer[i]!;
      },
    }),
  };
}

export function orderBy<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): OrderByOp<T, K> {
  return {
    kind: "orderBy",
    keySelector,
    queryable,
    apply: (source) => new OrderedIter(source, [ascendingComparer(keySelector)]),
  };
}

export function orderByDescending<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): OrderByDescendingOp<T, K> {
  return {
    kind: "orderByDescending",
    keySelector,
    queryable,
    apply: (source) => new OrderedIter(source, [descendingComparer(keySelector)]),
  };
}

/**
 * Secondary sort applied after `orderBy` / `orderByDescending`. Reaches
 * back into the upstream `OrderedIter` to extend its comparer list, so
 * the final sort uses every key in declaration order. Calling `thenBy`
 * without a prior orderBy degenerates to a single-key sort by the
 * provided key.
 */
export function thenBy<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): ThenByOp<T, K> {
  return {
    kind: "thenBy",
    keySelector,
    queryable,
    apply: (source) => {
      const cmp = ascendingComparer(keySelector);
      return source instanceof OrderedIter
        ? new OrderedIter(source.source, [...source.comparers, cmp])
        : new OrderedIter(source, [cmp]);
    },
  };
}

export function thenByDescending<T, K>(
  keySelector: (item: T) => K,
  queryable?: QueryableMeta,
): ThenByDescendingOp<T, K> {
  return {
    kind: "thenByDescending",
    keySelector,
    queryable,
    apply: (source) => {
      const cmp = descendingComparer(keySelector);
      return source instanceof OrderedIter
        ? new OrderedIter(source.source, [...source.comparers, cmp])
        : new OrderedIter(source, [cmp]);
    },
  };
}

export function zip<T, U, R>(
  other: Iterable<U>,
  resultSelector: (first: T, second: U) => R,
  queryable?: QueryableMeta,
): ZipOp<T, U, R> {
  return {
    kind: "zip",
    other,
    resultSelector,
    queryable,
    apply: (source) => ({
      *[Symbol.iterator]() {
        const itA = source[Symbol.iterator]();
        const itB = other[Symbol.iterator]();
        while (true) {
          const a = itA.next();
          const b = itB.next();
          if (a.done || b.done) return;
          yield resultSelector(a.value, b.value);
        }
      },
    }),
  };
}
