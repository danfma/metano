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
