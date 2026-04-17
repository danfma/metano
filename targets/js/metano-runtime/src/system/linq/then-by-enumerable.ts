import { EnumerableBase } from "./enumerable-base.ts";
import { compareKeys } from "./compare-keys.ts";

/** Secondary sort: sorts elements by a key, preserving the order of the previous sort for equal keys. */
export class ThenByEnumerable<T, K> extends EnumerableBase<T> {
  constructor(
    readonly source: EnumerableBase<T>,
    readonly keySelector: (item: T) => K,
    readonly descending: boolean,
  ) {
    super();
  }

  *[Symbol.iterator](): Iterator<T> {
    // Materialize and collect all comparators from the chain
    const comparators = collectComparators<T>(this);
    const items = materializeSource<T>(this);
    items.sort((a, b) => {
      for (const cmp of comparators) {
        const result = cmp(a, b);
        if (result !== 0) return result;
      }
      return 0;
    });
    yield* items;
  }
}

/** Walks the chain of OrderBy/ThenBy to collect all comparators in order. */
function collectComparators<T>(node: EnumerableBase<T>): ((a: T, b: T) => number)[] {
  const comparators: ((a: T, b: T) => number)[] = [];
  let current: EnumerableBase<T> = node;

  while (current instanceof ThenByEnumerable) {
    const thenBy = current as ThenByEnumerable<T, unknown>;
    comparators.unshift(makeComparator(thenBy.keySelector, thenBy.descending));
    current = thenBy.source;
  }

  // The root should be an OrderByEnumerable-like (has keySelector + descending)
  if ("keySelector" in current && "descending" in current) {
    const orderBy = current as unknown as {
      keySelector: (item: T) => unknown;
      descending: boolean;
      source: EnumerableBase<T>;
    };
    comparators.unshift(makeComparator(orderBy.keySelector, orderBy.descending));
  }

  return comparators;
}

/** Walks to the root source (past all OrderBy/ThenBy) and materializes it. */
function materializeSource<T>(node: EnumerableBase<T>): T[] {
  let current: EnumerableBase<T> = node;
  while (current instanceof ThenByEnumerable) {
    current = (current as ThenByEnumerable<T, unknown>).source;
  }
  if ("source" in current) {
    return [...(current as { source: EnumerableBase<T> }).source];
  }
  return [...current];
}

function makeComparator<T>(
  keySelector: (item: T) => unknown,
  descending: boolean,
): (a: T, b: T) => number {
  return (a, b) => {
    const cmp = compareKeys(keySelector(a), keySelector(b));
    return descending ? -cmp : cmp;
  };
}
