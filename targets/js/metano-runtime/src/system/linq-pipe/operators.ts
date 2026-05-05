/**
 * Composition operators (lazy + re-enumerable) — each returns an
 * {@link OperatorFn} that produces a fresh `Iterable<R>`. The generator
 * lives behind `[Symbol.iterator]` so every `for...of` over the result
 * runs the operator afresh against the source — matching .NET LINQ's
 * deferred + re-enumerable semantics.
 *
 * Without this wrapping, returning the generator function's result
 * directly would yield a single-use `Generator`, breaking re-enumeration:
 *
 * ```ts
 * const chain = pipe(arr, where(p));
 * for (const x of chain) { ... } // works
 * for (const x of chain) { ... } // empty if `chain` were a Generator
 * ```
 *
 * Short-circuit terminals (`first`, `any`, `take(n).first()`) still stop
 * iterating as soon as their result is determined.
 */
import type { OperatorFn } from "./types.ts";

export function where<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      let i = 0;
      for (const item of source) if (predicate(item, i++)) yield item;
    },
  });
}

export function select<T, R>(selector: (item: T, index: number) => R): OperatorFn<T, R> {
  return (source) => ({
    *[Symbol.iterator]() {
      let i = 0;
      for (const item of source) yield selector(item, i++);
    },
  });
}

export function selectMany<T, R>(
  selector: (item: T, index: number) => Iterable<R>,
): OperatorFn<T, R> {
  return (source) => ({
    *[Symbol.iterator]() {
      let i = 0;
      for (const item of source) for (const inner of selector(item, i++)) yield inner;
    },
  });
}

export function take<T>(count: number): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      if (count <= 0) return;
      let n = 0;
      for (const item of source) {
        yield item;
        if (++n >= count) return;
      }
    },
  });
}

export function skip<T>(count: number): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      let n = 0;
      for (const item of source) {
        if (n++ < count) continue;
        yield item;
      }
    },
  });
}

export function takeWhile<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      let i = 0;
      for (const item of source) {
        if (!predicate(item, i++)) return;
        yield item;
      }
    },
  });
}

export function skipWhile<T>(predicate: (item: T, index: number) => boolean): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      let i = 0;
      let skipping = true;
      for (const item of source) {
        if (skipping && predicate(item, i++)) continue;
        skipping = false;
        yield item;
      }
    },
  });
}

export function distinct<T>(): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      const seen = new Set<T>();
      for (const item of source)
        if (!seen.has(item)) {
          seen.add(item);
          yield item;
        }
    },
  });
}

export function distinctBy<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return (source) => ({
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
  });
}

export function concat<T>(other: Iterable<T>): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      for (const item of source) yield item;
      for (const item of other) yield item;
    },
  });
}

export function append<T>(element: T): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      for (const item of source) yield item;
      yield element;
    },
  });
}

export function prepend<T>(element: T): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      yield element;
      for (const item of source) yield item;
    },
  });
}

export function reverse<T>(): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      const buffer = [...source];
      for (let i = buffer.length - 1; i >= 0; i--) yield buffer[i]!;
    },
  });
}

/**
 * `orderBy` materializes the source on every enumeration to sort it. Same
 * laziness shape as .NET: nothing happens at composition time, the buffer
 * is allocated on first iteration of the result and discarded afterwards.
 * Re-enumerating runs the sort again against the (possibly mutated) source.
 */
export function orderBy<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      const buffer = [...source];
      buffer.sort((a, b) => {
        const ka = keySelector(a);
        const kb = keySelector(b);
        return ka < kb ? -1 : ka > kb ? 1 : 0;
      });
      for (const item of buffer) yield item;
    },
  });
}

export function orderByDescending<T, K>(keySelector: (item: T) => K): OperatorFn<T, T> {
  return (source) => ({
    *[Symbol.iterator]() {
      const buffer = [...source];
      buffer.sort((a, b) => {
        const ka = keySelector(a);
        const kb = keySelector(b);
        return ka < kb ? 1 : ka > kb ? -1 : 0;
      });
      for (const item of buffer) yield item;
    },
  });
}

export function zip<T, U, R>(
  other: Iterable<U>,
  resultSelector: (first: T, second: U) => R,
): OperatorFn<T, R> {
  return (source) => ({
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
  });
}
