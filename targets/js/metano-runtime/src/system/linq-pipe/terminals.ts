/**
 * Terminal factories — each returns a tagged descriptor whose `apply`
 * walks the source and produces a concrete value (array, scalar,
 * boolean). Same descriptor shape as composition operators so an
 * IQueryable provider can switch over the terminal kind exhaustively
 * (e.g. translate `count` to `SELECT COUNT(*)`, `sum` to
 * `SELECT SUM(<selector>)`, etc.).
 *
 * Short-circuit terminals (`first`, `any`, `contains`) stop iterating
 * as soon as the answer is known.
 */
import type {
  AggregateTerm,
  AllTerm,
  AnyTerm,
  AverageTerm,
  ContainsTerm,
  CountTerm,
  FirstOrDefaultTerm,
  FirstTerm,
  LastOrDefaultTerm,
  LastTerm,
  MaxTerm,
  MinTerm,
  SingleTerm,
  SumTerm,
  ToArrayTerm,
  ToMapTerm,
  ToSetTerm,
} from "./types.ts";

export function toArray<T>(): ToArrayTerm<T> {
  return {
    kind: "toArray",
    apply: (source) => [...source],
  };
}

export function toMap<T, K, V>(
  keySelector: (item: T) => K,
  valueSelector: (item: T) => V,
): ToMapTerm<T, K, V> {
  return {
    kind: "toMap",
    keySelector,
    valueSelector,
    apply: (source) => {
      const m = new Map<K, V>();
      for (const item of source) m.set(keySelector(item), valueSelector(item));
      return m;
    },
  };
}

export function toSet<T>(): ToSetTerm<T> {
  return {
    kind: "toSet",
    apply: (source) => new Set(source),
  };
}

export function first<T>(predicate?: (item: T) => boolean): FirstTerm<T> {
  return {
    kind: "first",
    predicate,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return item;
      throw new Error("Sequence contains no matching element");
    },
  };
}

export function firstOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
): FirstOrDefaultTerm<T> {
  return {
    kind: "firstOrDefault",
    predicate,
    defaultValue,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return item;
      return defaultValue;
    },
  };
}

export function last<T>(predicate?: (item: T) => boolean): LastTerm<T> {
  return {
    kind: "last",
    predicate,
    apply: (source) => {
      let result: T | undefined;
      let found = false;
      for (const item of source)
        if (!predicate || predicate(item)) {
          result = item;
          found = true;
        }
      if (!found) throw new Error("Sequence contains no matching element");
      return result!;
    },
  };
}

export function lastOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
): LastOrDefaultTerm<T> {
  return {
    kind: "lastOrDefault",
    predicate,
    defaultValue,
    apply: (source) => {
      let result: T | null = defaultValue;
      for (const item of source) if (!predicate || predicate(item)) result = item;
      return result;
    },
  };
}

export function single<T>(predicate?: (item: T) => boolean): SingleTerm<T> {
  return {
    kind: "single",
    predicate,
    apply: (source) => {
      let result: T | undefined;
      let count = 0;
      for (const item of source)
        if (!predicate || predicate(item)) {
          result = item;
          if (++count > 1) throw new Error("Sequence contains more than one matching element");
        }
      if (count === 0) throw new Error("Sequence contains no matching element");
      return result!;
    },
  };
}

export function any<T>(predicate?: (item: T) => boolean): AnyTerm<T> {
  return {
    kind: "any",
    predicate,
    apply: (source) => {
      for (const item of source) if (!predicate || predicate(item)) return true;
      return false;
    },
  };
}

export function all<T>(predicate: (item: T) => boolean): AllTerm<T> {
  return {
    kind: "all",
    predicate,
    apply: (source) => {
      for (const item of source) if (!predicate(item)) return false;
      return true;
    },
  };
}

export function count<T>(predicate?: (item: T) => boolean): CountTerm<T> {
  return {
    kind: "count",
    predicate,
    apply: (source) => {
      let n = 0;
      for (const item of source) if (!predicate || predicate(item)) n++;
      return n;
    },
  };
}

export function sum<T>(selector?: (item: T) => number): SumTerm<T> {
  return {
    kind: "sum",
    selector,
    apply: (source) => {
      let total = 0;
      for (const item of source) total += selector ? selector(item) : (item as unknown as number);
      return total;
    },
  };
}

export function min<T>(selector?: (item: T) => number): MinTerm<T> {
  return {
    kind: "min",
    selector,
    apply: (source) => {
      let result = Number.POSITIVE_INFINITY;
      for (const item of source) {
        const v = selector ? selector(item) : (item as unknown as number);
        if (v < result) result = v;
      }
      return result;
    },
  };
}

export function max<T>(selector?: (item: T) => number): MaxTerm<T> {
  return {
    kind: "max",
    selector,
    apply: (source) => {
      let result = Number.NEGATIVE_INFINITY;
      for (const item of source) {
        const v = selector ? selector(item) : (item as unknown as number);
        if (v > result) result = v;
      }
      return result;
    },
  };
}

export function average<T>(selector?: (item: T) => number): AverageTerm<T> {
  return {
    kind: "average",
    selector,
    apply: (source) => {
      let total = 0;
      let n = 0;
      for (const item of source) {
        total += selector ? selector(item) : (item as unknown as number);
        n++;
      }
      if (n === 0) throw new Error("Sequence contains no elements");
      return total / n;
    },
  };
}

export function contains<T>(value: T): ContainsTerm<T> {
  return {
    kind: "contains",
    value,
    apply: (source) => {
      for (const element of source) if (element === value) return true;
      return false;
    },
  };
}

export function aggregate<T, A>(
  seed: A,
  accumulator: (acc: A, item: T) => A,
): AggregateTerm<T, A> {
  return {
    kind: "aggregate",
    seed,
    accumulator,
    apply: (source) => {
      let result = seed;
      for (const item of source) result = accumulator(result, item);
      return result;
    },
  };
}
