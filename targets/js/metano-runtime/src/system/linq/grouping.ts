/**
 * The key bucketing `groupBy`, `toLookup` and `join` share: the elements of a source
 * under the equality .NET uses for a key (`EqualityComparer<K>.Default`, or a user
 * comparer), in the order the keys are first seen, each bucket in source order.
 */
import type { IEqualityComparer } from "../collections/default-comparer.ts";
import { HashCore } from "../collections/hash-core.ts";
import type { Grouping } from "./types.ts";

/**
 * `comparer` as .NET's `Lookup` uses it: a `null` key hashes to 0 without asking the
 * comparer, which may throw on it (`GetHashCode(string value) => value.Length`).
 */
function nullSafe<K>(comparer: IEqualityComparer<K> | null | undefined) {
  return comparer === null || comparer === undefined
    ? comparer
    : {
        equals: (a: K, b: K) => comparer.equals(a, b),
        getHashCode: (key: K) =>
          key === null || key === undefined ? 0 : comparer.getHashCode(key),
      };
}

/** Elements of `source` bucketed by `keySelector`; a `null` key is skipped when `skipNullKeys`. */
export function bucketsOf<T, K, V = T>(
  source: Iterable<T>,
  keySelector: (item: T) => K,
  comparer: IEqualityComparer<K> | null | undefined,
  elementSelector?: (item: T) => V,
  skipNullKeys = false,
): HashCore<K, V[]> {
  const buckets = new HashCore<K, V[]>(nullSafe(comparer));

  if (elementSelector === undefined && !skipNullKeys && comparer) {
    return bucketAll(
      source,
      keySelector,
      buckets as unknown as HashCore<K, T[]>,
    ) as unknown as HashCore<K, V[]>;
  }

  for (const item of source) {
    const key = keySelector(item);

    if (skipNullKeys && (key === null || key === undefined)) {
      continue;
    }

    buckets.append(
      key,
      elementSelector === undefined ? (item as unknown as V) : elementSelector(item),
    );
  }

  return buckets;
}

function bucketAll<T, K>(
  source: Iterable<T>,
  keySelector: (item: T) => K,
  buckets: HashCore<K, T[]>,
): HashCore<K, T[]> {
  for (const item of source) {
    buckets.append(keySelector(item), item);
  }

  return buckets;
}

/** The groupings of `buckets`: an iterable bucket that also reads `.key`. */
export function* groupingsOf<K, T>(buckets: HashCore<K, T[]>): Generator<Grouping<K, T>> {
  for (const [key, items] of buckets) {
    // `Grouping` is `Iterable<T> & { key }`: the bucket array itself carries the key, so
    // consumers can `for…of` the grouping and read `.key`.
    yield Object.assign(items, { key }) as unknown as Grouping<K, T>;
  }
}
