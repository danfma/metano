/**
 * Composition operators (lazy) — each returns an {@link OperatorFn} that
 * walks its source via `Symbol.iterator`. No intermediate arrays;
 * short-circuit terminals (`first`, `take(n).first()`) stop iterating as
 * soon as their result is determined.
 *
 * Prototype subset for #20. Mirrors the legacy {@link EnumerableBase}
 * methods but as standalone tree-shakeable functions.
 */
import type { OperatorFn } from "./types.ts";

export function where<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return function* (source) {
    let i = 0;
    for (const item of source) if (predicate(item, i++)) yield item;
  };
}

export function select<T, R>(selector: (item: T, index: number) => R): OperatorFn<T, R> {
  return function* (source) {
    let i = 0;
    for (const item of source) yield selector(item, i++);
  };
}

export function selectMany<T, R>(
  selector: (item: T, index: number) => Iterable<R>,
): OperatorFn<T, R> {
  return function* (source) {
    let i = 0;
    for (const item of source) for (const inner of selector(item, i++)) yield inner;
  };
}

export function take<T>(count: number): OperatorFn<T, T> {
  return function* (source) {
    if (count <= 0) return;
    let n = 0;
    for (const item of source) {
      yield item;
      if (++n >= count) return;
    }
  };
}

export function skip<T>(count: number): OperatorFn<T, T> {
  return function* (source) {
    let n = 0;
    for (const item of source) {
      if (n++ < count) continue;
      yield item;
    }
  };
}

export function takeWhile<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return function* (source) {
    let i = 0;
    for (const item of source) {
      if (!predicate(item, i++)) return;
      yield item;
    }
  };
}

export function skipWhile<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return function* (source) {
    let i = 0;
    let skipping = true;
    for (const item of source) {
      if (skipping && predicate(item, i++)) continue;
      skipping = false;
      yield item;
    }
  };
}

export function distinct<T>(): OperatorFn<T, T> {
  return function* (source) {
    const seen = new Set<T>();
    for (const item of source)
      if (!seen.has(item)) {
        seen.add(item);
        yield item;
      }
  };
}

export function distinctBy<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return function* (source) {
    const seen = new Set<K>();
    for (const item of source) {
      const key = keySelector(item);
      if (!seen.has(key)) {
        seen.add(key);
        yield item;
      }
    }
  };
}

export function concat<T>(other: Iterable<T>): OperatorFn<T, T> {
  return function* (source) {
    for (const item of source) yield item;
    for (const item of other) yield item;
  };
}

export function append<T>(element: T): OperatorFn<T, T> {
  return function* (source) {
    for (const item of source) yield item;
    yield element;
  };
}

export function prepend<T>(element: T): OperatorFn<T, T> {
  return function* (source) {
    yield element;
    for (const item of source) yield item;
  };
}

export function reverse<T>(): OperatorFn<T, T> {
  return function* (source) {
    const buffer = [...source];
    for (let i = buffer.length - 1; i >= 0; i--) yield buffer[i]!;
  };
}

/** Eager: materializes the source to sort. Lazy on output via generator. */
export function orderBy<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return function* (source) {
    const buffer = [...source];
    buffer.sort((a, b) => {
      const ka = keySelector(a);
      const kb = keySelector(b);
      return ka < kb ? -1 : ka > kb ? 1 : 0;
    });
    for (const item of buffer) yield item;
  };
}

export function orderByDescending<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return function* (source) {
    const buffer = [...source];
    buffer.sort((a, b) => {
      const ka = keySelector(a);
      const kb = keySelector(b);
      return ka < kb ? 1 : ka > kb ? -1 : 0;
    });
    for (const item of buffer) yield item;
  };
}

export function zip<T, U, R>(
  other: Iterable<U>,
  resultSelector: (first: T, second: U) => R,
): OperatorFn<T, R> {
  return function* (source) {
    const itA = source[Symbol.iterator]();
    const itB = other[Symbol.iterator]();
    while (true) {
      const a = itA.next();
      const b = itB.next();
      if (a.done || b.done) return;
      yield resultSelector(a.value, b.value);
    }
  };
}
