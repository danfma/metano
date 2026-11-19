/**
 * Runtime helpers for the C# `Dictionary<TKey, TValue>` / `IReadOnlyDictionary<TKey,
 * TValue>` family of methods whose lowering can't be expressed as a simple inline JS
 * expression. The compiler imports these via the declarative mappings under
 * `Metano/Runtime/Dictionaries.cs`.
 */

/**
 * Returns the value stored under `key`, or `defaultValue` when the key is absent.
 * Mirrors `CollectionExtensions.GetValueOrDefault(dictionary, key, defaultValue)`.
 *
 * Membership is tested with `has` rather than `get(key) ?? defaultValue`, so a key that
 * is present with a stored `null` value returns that `null`, as it does in C#. The
 * compiler passes `default(TValue)` (`0`, `false`, `null`, …) as `defaultValue` for the
 * single-argument overload.
 */
export function getValueOrDefault<K, V>(map: ReadonlyMap<K, V>, key: K, defaultValue: V): V {
  return map.has(key) ? (map.get(key) as V) : defaultValue;
}

/**
 * Looks `key` up once and reports whether it was present, mirroring
 * `IDictionary<TKey, TValue>.TryGetValue(key, out value)`. The compiler lowers the `out`
 * parameter into the second tuple element and passes `default(TValue)` (`0`, `false`,
 * `null`, …) as `missingValue`, which is what C# stores in `value` on a miss.
 *
 * Membership is tested with `has`, so a key stored with a `null` or `undefined` value is
 * reported as found. Destructure the result with `const` so TypeScript narrows `value` on
 * `found`.
 */
export function tryGetValue<K, V, D>(
  map: ReadonlyMap<K, V>,
  key: K,
  missingValue: D,
): readonly [found: true, value: V] | readonly [found: false, value: D] {
  return map.has(key) ? [true, map.get(key) as V] : [false, missingValue];
}
