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
import type { ExprTree } from "./expr-tree.ts";
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
  WhereOp,
  ZipOp,
} from "./types.ts";

export function where<T>(
  predicate: (item: T, index: number) => boolean,
  expression?: ExprTree,
): WhereOp<T> {
  return {
    kind: "where",
    predicate,
    expression,
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
  expression?: ExprTree,
): SelectOp<T, R> {
  return {
    kind: "select",
    selector,
    expression,
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
  expression?: ExprTree,
): SelectManyOp<T, R> {
  return {
    kind: "selectMany",
    selector,
    expression,
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
  expression?: ExprTree,
): TakeWhileOp<T> {
  return {
    kind: "takeWhile",
    predicate,
    expression,
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
  expression?: ExprTree,
): SkipWhileOp<T> {
  return {
    kind: "skipWhile",
    predicate,
    expression,
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
  expression?: ExprTree,
): DistinctByOp<T, K> {
  return {
    kind: "distinctBy",
    keySelector,
    expression,
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
  expression?: ExprTree,
): OrderByOp<T, K> {
  return {
    kind: "orderBy",
    keySelector,
    expression,
    apply: (source) => ({
      *[Symbol.iterator]() {
        const buffer = [...source];
        buffer.sort((a, b) => {
          const ka = keySelector(a);
          const kb = keySelector(b);
          return ka < kb ? -1 : ka > kb ? 1 : 0;
        });
        for (const item of buffer) yield item;
      },
    }),
  };
}

export function orderByDescending<T, K>(
  keySelector: (item: T) => K,
  expression?: ExprTree,
): OrderByDescendingOp<T, K> {
  return {
    kind: "orderByDescending",
    keySelector,
    expression,
    apply: (source) => ({
      *[Symbol.iterator]() {
        const buffer = [...source];
        buffer.sort((a, b) => {
          const ka = keySelector(a);
          const kb = keySelector(b);
          return ka < kb ? 1 : ka > kb ? -1 : 0;
        });
        for (const item of buffer) yield item;
      },
    }),
  };
}

export function zip<T, U, R>(
  other: Iterable<U>,
  resultSelector: (first: T, second: U) => R,
  expression?: ExprTree,
): ZipOp<T, U, R> {
  return {
    kind: "zip",
    other,
    resultSelector,
    expression,
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
