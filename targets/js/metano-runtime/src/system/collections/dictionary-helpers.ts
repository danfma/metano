/**
 * `CollectionExtensions.GetValueOrDefault` fallback for a non-runtime dictionary
 * receiver. Runtime dictionary classes use their own members; the compiler still
 * imports this function via `Metano/Runtime/Dictionaries.cs` for other receivers.
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
