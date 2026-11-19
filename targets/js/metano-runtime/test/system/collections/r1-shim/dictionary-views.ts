// TEMPORARY test-only stand-in for R1's `dictionary-views.ts` (see preload.ts).
import { elementEquals } from "./default-comparer.ts";

export interface KeyValueSource<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number;
  containsKey(key: K): boolean;
}

export class KeyCollection<K> {
  constructor(private readonly source: KeyValueSource<K, unknown>) {}

  get count(): number {
    return this.source.count;
  }

  contains(key: K): boolean {
    return this.source.containsKey(key);
  }

  toArray(): K[] {
    return [...this];
  }

  *[Symbol.iterator](): Iterator<K> {
    for (const [key] of this.source) {
      yield key;
    }
  }
}

export class ValueCollection<V> {
  constructor(private readonly source: KeyValueSource<unknown, V>) {}

  get count(): number {
    return this.source.count;
  }

  contains(value: V): boolean {
    for (const [, candidate] of this.source) {
      if (elementEquals(candidate, value)) {
        return true;
      }
    }

    return false;
  }

  toArray(): V[] {
    return [...this];
  }

  *[Symbol.iterator](): Iterator<V> {
    for (const [, value] of this.source) {
      yield value;
    }
  }
}
