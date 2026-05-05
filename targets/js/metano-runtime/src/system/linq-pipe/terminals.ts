/**
 * Terminal operators (eager) — each returns a {@link TerminalFn} that
 * walks the source and produces a concrete value (array, scalar,
 * boolean). Short-circuit terminals (`first`, `any`, `contains`) stop
 * iterating as soon as the answer is known.
 *
 * Prototype subset for #20.
 */
import type { TerminalFn } from "./types.ts";

export function toArray<T>(): TerminalFn<T, T[]> {
  return (source) => [...source];
}

export function toMap<T, K, V>(
  keySelector: (item: T) => K,
  valueSelector: (item: T) => V,
): TerminalFn<T, Map<K, V>> {
  return (source) => {
    const m = new Map<K, V>();
    for (const item of source) m.set(keySelector(item), valueSelector(item));
    return m;
  };
}

export function toSet<T>(): TerminalFn<T, Set<T>> {
  return (source) => new Set(source);
}

export function first<T>(predicate?: (item: T) => boolean): TerminalFn<T, T> {
  return (source) => {
    for (const item of source) if (!predicate || predicate(item)) return item;
    throw new Error("Sequence contains no matching element");
  };
}

export function firstOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
): TerminalFn<T, T | null> {
  return (source) => {
    for (const item of source) if (!predicate || predicate(item)) return item;
    return defaultValue;
  };
}

export function last<T>(predicate?: (item: T) => boolean): TerminalFn<T, T> {
  return (source) => {
    let result: T | undefined;
    let found = false;
    for (const item of source)
      if (!predicate || predicate(item)) {
        result = item;
        found = true;
      }
    if (!found) throw new Error("Sequence contains no matching element");
    return result!;
  };
}

export function lastOrDefault<T>(
  predicate?: (item: T) => boolean,
  defaultValue: T | null = null,
): TerminalFn<T, T | null> {
  return (source) => {
    let result: T | null = defaultValue;
    for (const item of source) if (!predicate || predicate(item)) result = item;
    return result;
  };
}

export function single<T>(predicate?: (item: T) => boolean): TerminalFn<T, T> {
  return (source) => {
    let result: T | undefined;
    let count = 0;
    for (const item of source)
      if (!predicate || predicate(item)) {
        result = item;
        if (++count > 1) throw new Error("Sequence contains more than one matching element");
      }
    if (count === 0) throw new Error("Sequence contains no matching element");
    return result!;
  };
}

export function any<T>(predicate?: (item: T) => boolean): TerminalFn<T, boolean> {
  return (source) => {
    for (const item of source) if (!predicate || predicate(item)) return true;
    return false;
  };
}

export function all<T>(predicate: (item: T) => boolean): TerminalFn<T, boolean> {
  return (source) => {
    for (const item of source) if (!predicate(item)) return false;
    return true;
  };
}

export function count<T>(predicate?: (item: T) => boolean): TerminalFn<T, number> {
  return (source) => {
    let n = 0;
    for (const item of source) if (!predicate || predicate(item)) n++;
    return n;
  };
}

export function sum<T>(selector?: (item: T) => number): TerminalFn<T, number> {
  return (source) => {
    let total = 0;
    for (const item of source) total += selector ? selector(item) : (item as unknown as number);
    return total;
  };
}

export function min<T>(selector?: (item: T) => number): TerminalFn<T, number> {
  return (source) => {
    let result = Number.POSITIVE_INFINITY;
    for (const item of source) {
      const v = selector ? selector(item) : (item as unknown as number);
      if (v < result) result = v;
    }
    return result;
  };
}

export function max<T>(selector?: (item: T) => number): TerminalFn<T, number> {
  return (source) => {
    let result = Number.NEGATIVE_INFINITY;
    for (const item of source) {
      const v = selector ? selector(item) : (item as unknown as number);
      if (v > result) result = v;
    }
    return result;
  };
}

export function average<T>(selector?: (item: T) => number): TerminalFn<T, number> {
  return (source) => {
    let total = 0;
    let n = 0;
    for (const item of source) {
      total += selector ? selector(item) : (item as unknown as number);
      n++;
    }
    if (n === 0) throw new Error("Sequence contains no elements");
    return total / n;
  };
}

export function contains<T>(item: T): TerminalFn<T, boolean> {
  return (source) => {
    for (const element of source) if (element === item) return true;
    return false;
  };
}

export function aggregate<T, A>(
  seed: A,
  accumulator: (acc: A, item: T) => A,
): TerminalFn<T, A> {
  return (source) => {
    let result = seed;
    for (const item of source) result = accumulator(result, item);
    return result;
  };
}
