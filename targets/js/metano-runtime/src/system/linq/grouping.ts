/**
 * The key bucketing `groupBy`, `toLookup` and `join` share: the elements of a source
 * under the equality .NET uses for a key (`EqualityComparer<K>.Default`, or a user
 * comparer), in the order the keys are first seen, each bucket in source order.
 */
import type { IEqualityComparer } from "../collections/default-comparer.ts";
import { HashCore } from "../collections/hash-core.ts";
import type { Grouping } from "./types.ts";

/** Elements of `source` bucketed by `keySelector`; a `null` key is skipped when `skipNullKeys`. */
export function bucketsOf<T, K, V = T>(
  source: Iterable<T>,
  keySelector: (item: T) => K,
  comparer: IEqualityComparer<K> | null | undefined,
  elementSelector?: (item: T) => V,
  skipNullKeys = false,
): HashCore<K, V[]> {
  const buckets = new HashCore<K, V[]>(comparer);

  for (const item of source) {
    const key = keySelector(item);

    if (skipNullKeys && (key === null || key === undefined)) {
      continue;
    }

    const element = (elementSelector === undefined ? item : elementSelector(item)) as V;
    const bucket = buckets.get(key);

    if (bucket === undefined) {
      buckets.set(key, [element]);
    } else {
      bucket.push(element);
    }
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
